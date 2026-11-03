using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Application.Options;
using Support360.Application.Security;
using Support360.Contracts.Agents;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class AgentService : IAgentService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretGenerator _secretGenerator;
    private readonly ISecretHasher _secretHasher;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IRealtimeNotifier _realtime;
    private readonly AgentPlatformOptions _options;

    public AgentService(
        ISupport360DbContext db,
        ITenantContext tenantContext,
        ISecretGenerator secretGenerator,
        ISecretHasher secretHasher,
        IClock clock,
        IAuditService audit,
        IRealtimeNotifier realtime,
        IOptions<AgentPlatformOptions> options)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretGenerator = secretGenerator;
        _secretHasher = secretHasher;
        _clock = clock;
        _audit = audit;
        _realtime = realtime;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<AgentSummaryDto>> ListAgentsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var now = _clock.UtcNow;
        var since = now.AddMinutes(-15);

        var agents = await _db.Agents
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.LastHeartbeatAt)
            .ThenBy(a => a.Hostname)
            .ToListAsync(cancellationToken);

        var samples = await _db.MetricSamples
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.CapturedAt >= since)
            .Select(s => new { s.AgentId, s.Metric, s.Value, s.CapturedAt })
            .ToListAsync(cancellationToken);

        var latest = samples
            .GroupBy(s => (s.AgentId, s.Metric))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CapturedAt).First().Value);

        var alertCounts = await _db.Alerts
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && (a.Status == AlertStatus.Active || a.Status == AlertStatus.Acknowledged))
            .GroupBy(a => a.AgentId)
            .Select(g => new { AgentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AgentId, x => x.Count, cancellationToken);

        return agents
            .Select(a => MapSummary(a, latest, alertCounts.GetValueOrDefault(a.Id), now))
            .ToArray();
    }

    public async Task<AgentDetailResponse?> GetAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var now = _clock.UtcNow;
        var since = now.AddMinutes(-15);

        var agent = await _db.Agents
            .AsNoTracking()
            .Include(a => a.Credentials)
            .SingleOrDefaultAsync(a => a.TenantId == tenantId && a.Id == agentId, cancellationToken);

        if (agent is null)
        {
            return null;
        }

        var services = await _db.AgentServiceStatuses
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.AgentId == agentId)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var samples = await _db.MetricSamples
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.AgentId == agentId && s.CapturedAt >= since)
            .Select(s => new { s.Metric, s.Value, s.CapturedAt })
            .ToListAsync(cancellationToken);

        var latest = samples
            .GroupBy(s => s.Metric)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CapturedAt).First().Value);

        var alertCount = await _db.Alerts
            .AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId && a.AgentId == agentId
                             && (a.Status == AlertStatus.Active || a.Status == AlertStatus.Acknowledged), cancellationToken);

        var capabilities = ParseCapabilities(agent.CapabilitiesJson);

        return new AgentDetailResponse(
            MapSummary(agent, latest.ToDictionary(k => (agentId, k.Key), v => v.Value), alertCount, now),
            services.Select(s => new ServiceStatusDto(s.Name, s.Kind, s.State.ToString(), s.CheckedAt, s.Detail)).ToArray(),
            capabilities,
            agent.Credentials.OrderByDescending(c => c.CreatedAt).Select(c => new AgentCredentialDto(
                c.Id, c.KeyPrefix, c.CreatedAt, c.ExpiresAt, c.RevokedAt,
                c.ExpiresAt is not null && c.ExpiresAt <= now)).ToArray(),
            agent.DeploymentExecutionAllowed);
    }

    public async Task<AgentMetricsResponse> GetMetricsAsync(
        Guid tenantId,
        Guid agentId,
        string range,
        CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var now = _clock.UtcNow;
        var (canonicalRange, since) = ParseRange(range, now);

        var exists = await _db.Agents.AsNoTracking().AnyAsync(a => a.TenantId == tenantId && a.Id == agentId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Agent not found.");
        }

        var cutoff = now.AddHours(-24);
        if (since < cutoff)
        {
            since = cutoff;
        }

        var samples = await _db.MetricSamples
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.AgentId == agentId && s.CapturedAt >= since)
            .OrderBy(s => s.CapturedAt)
            .Select(s => new { s.Metric, s.Value, s.CapturedAt })
            .ToListAsync(cancellationToken);

        var series = samples
            .GroupBy(s => s.Metric)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AgentMetricPointDto>)g.TakeLast(720)
                    .Select(s => new AgentMetricPointDto(s.CapturedAt, s.Value)).ToArray());

        var latest = samples
            .GroupBy(s => s.Metric)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CapturedAt).First().Value);

        return new AgentMetricsResponse(agentId, canonicalRange, series, latest);
    }

    public async Task<RotateCredentialResponse> RotateCredentialAsync(
        RequestActor actor,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(actor.TenantId);
        var agent = await _db.Agents
            .Include(a => a.Credentials)
            .SingleOrDefaultAsync(a => a.TenantId == actor.TenantId && a.Id == agentId, cancellationToken)
            ?? throw new NotFoundException("Agent not found.");

        if (agent.Status == AgentStatus.Revoked)
        {
            throw new ConflictException("A revoked agent cannot receive new credentials.");
        }

        var now = _clock.UtcNow;
        foreach (var oldCredential in agent.Credentials.Where(c => c.RevokedAt is null))
        {
            oldCredential.RevokedAt = now;
            oldCredential.RevokedReason = "rotated";
            oldCredential.UpdatedAt = now;
        }

        var apiKey = _secretGenerator.Generate(48);
        var credential = new AgentCredential
        {
            TenantId = actor.TenantId,
            AgentId = agent.Id,
            KeyHash = _secretHasher.Hash(apiKey),
            KeyPrefix = apiKey[..8],
            ExpiresAt = _options.AgentCredentialLifetimeDays > 0 ? now.AddDays(_options.AgentCredentialLifetimeDays) : null,
            CreatedAt = now
        };

        _db.AgentCredentials.Add(credential);
        agent.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            actor.TenantId,
            AuditActorType.User,
            actor.UserId,
            actor.DisplayName,
            "agent.credential.rotated",
            "Agent",
            agent.Id.ToString(),
            "success",
            new { credential.KeyPrefix },
            actor.IpAddress,
            cancellationToken);

        await _realtime.AgentChangedAsync(actor.TenantId, agent.Id, cancellationToken);
        return new RotateCredentialResponse(credential.Id, apiKey, credential.KeyPrefix, credential.CreatedAt);
    }

    public async Task<RevokeAgentResponse> RevokeAgentAsync(
        RequestActor actor,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(actor.TenantId);
        var agent = await _db.Agents
            .Include(a => a.Credentials)
            .SingleOrDefaultAsync(a => a.TenantId == actor.TenantId && a.Id == agentId, cancellationToken)
            ?? throw new NotFoundException("Agent not found.");

        var now = _clock.UtcNow;
        if (agent.Status != AgentStatus.Revoked)
        {
            agent.Revoke(now);
            foreach (var credential in agent.Credentials.Where(c => c.RevokedAt is null))
            {
                credential.RevokedAt = now;
                credential.RevokedReason = "agent revoked";
                credential.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await _audit.WriteAsync(
                actor.TenantId,
                AuditActorType.User,
                actor.UserId,
                actor.DisplayName,
                "agent.revoked",
                "Agent",
                agent.Id.ToString(),
                "success",
                new { agent.Hostname },
                actor.IpAddress,
                cancellationToken);
            await _realtime.AgentChangedAsync(actor.TenantId, agent.Id, cancellationToken);
        }

        return new RevokeAgentResponse(agent.Id, agent.Status.ToString(), now);
    }

    private AgentSummaryDto MapSummary(
        Agent agent,
        IReadOnlyDictionary<(Guid AgentId, string Metric), double> latest,
        int activeAlerts,
        DateTimeOffset now)
    {
        var online = agent.Status == AgentStatus.Active
                     && agent.LastHeartbeatAt is not null
                     && agent.LastHeartbeatAt >= now.AddSeconds(-_options.OfflineAfterSeconds);

        return new AgentSummaryDto(
            agent.Id,
            agent.Hostname,
            agent.Platform.ToString(),
            agent.OsVersion ?? string.Empty,
            agent.Architecture ?? string.Empty,
            agent.Version ?? string.Empty,
            agent.Status.ToString(),
            online ? AgentConnectivity.Online.ToString() : AgentConnectivity.Offline.ToString(),
            agent.EnrolledAt,
            agent.LastHeartbeatAt,
            agent.LastTelemetryAt,
            agent.CollectorHealthy,
            agent.UptimeSeconds,
            Get(agent.Id, MetricCatalog.CpuUsagePercent),
            Get(agent.Id, MetricCatalog.MemoryUsedPercent),
            Get(agent.Id, MetricCatalog.DiskUsedPercent),
            activeAlerts);

        double? Get(Guid agentId, string metric) =>
            latest.TryGetValue((agentId, metric), out var value) ? value : null;
    }

    private static (string Range, DateTimeOffset Since) ParseRange(string? range, DateTimeOffset now) =>
        (range?.Trim().ToLowerInvariant()) switch
        {
            "15m" => ("15m", now.AddMinutes(-15)),
            "6h" => ("6h", now.AddHours(-6)),
            "24h" => ("24h", now.AddHours(-24)),
            "7d" => ("7d", now.AddDays(-7)),
            _ => ("1h", now.AddHours(-1))
        };

    private static IReadOnlyList<string> ParseCapabilities(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
