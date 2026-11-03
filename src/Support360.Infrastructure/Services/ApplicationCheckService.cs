using Microsoft.EntityFrameworkCore;
using FluentValidation;
using FluentValidation.Results;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Application.Security;
using Support360.Contracts.Applications;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class ApplicationCheckService : IApplicationCheckService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IRealtimeNotifier _realtime;
    private readonly IValidator<CreateApplicationCheckRequest> _createValidator;
    private readonly IValidator<UpdateApplicationCheckRequest> _updateValidator;
    private readonly IValidator<ApplicationCheckResultsRequest> _resultsValidator;
    private readonly IValidator<UpdateHttpCheckTargetPolicyRequest> _policyValidator;

    public ApplicationCheckService(
        ISupport360DbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IAuditService audit,
        IRealtimeNotifier realtime,
        IValidator<CreateApplicationCheckRequest> createValidator,
        IValidator<UpdateApplicationCheckRequest> updateValidator,
        IValidator<ApplicationCheckResultsRequest> resultsValidator,
        IValidator<UpdateHttpCheckTargetPolicyRequest> policyValidator)
    {
        _db = db;
        _tenantContext = tenantContext;
        _clock = clock;
        _audit = audit;
        _realtime = realtime;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _resultsValidator = resultsValidator;
        _policyValidator = policyValidator;
    }

    public async Task<IReadOnlyList<ApplicationCheckDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var checks = await (from check in _db.ApplicationChecks.AsNoTracking()
                            join agent in _db.Agents.AsNoTracking() on check.AgentId equals agent.Id
                            where check.TenantId == tenantId
                            orderby check.Name
                            select new { Check = check, agent.Hostname })
            .ToListAsync(cancellationToken);
        return checks.Select(c => ToDto(c.Check, c.Hostname)).ToArray();
    }

    public async Task<ApplicationCheckDto> CreateAsync(
        RequestActor actor,
        CreateApplicationCheckRequest request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(actor.TenantId);
        var agent = await _db.Agents.AsNoTracking().SingleOrDefaultAsync(
            a => a.TenantId == actor.TenantId && a.Id == request.AgentId && a.Status == AgentStatus.Active,
            cancellationToken) ?? throw new NotFoundException("Active agent not found.");

        var type = Enum.Parse<ApplicationCheckType>(request.Type, true);
        var target = request.Target.Trim();
        await EnsureHttpTargetAllowedAsync(actor.TenantId, type, target, cancellationToken);
        var now = _clock.UtcNow;
        var check = new ApplicationCheck
        {
            TenantId = actor.TenantId,
            AgentId = agent.Id,
            Name = request.Name.Trim(),
            Type = type,
            Target = target,
            IntervalSeconds = request.IntervalSeconds,
            Enabled = request.Enabled,
            CreatedAt = now
        };
        _db.ApplicationChecks.Add(check);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "application-check.created", "ApplicationCheck", check.Id.ToString(), "success",
            new { check.AgentId, check.Name, Type = type.ToString(), check.Target, check.IntervalSeconds, check.Enabled },
            actor.IpAddress, cancellationToken);
        await _realtime.AgentChangedAsync(actor.TenantId, agent.Id, cancellationToken);
        return ToDto(check, agent.Hostname);
    }

    public async Task<ApplicationCheckDto?> UpdateAsync(
        RequestActor actor,
        Guid checkId,
        UpdateApplicationCheckRequest request,
        CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(actor.TenantId);
        var check = await _db.ApplicationChecks.SingleOrDefaultAsync(
            c => c.TenantId == actor.TenantId && c.Id == checkId, cancellationToken);
        if (check is null) return null;

        var previous = new { check.Name, check.Type, check.Target, check.IntervalSeconds, check.Enabled };
        var type = Enum.Parse<ApplicationCheckType>(request.Type, true);
        var target = request.Target.Trim();
        await EnsureHttpTargetAllowedAsync(actor.TenantId, type, target, cancellationToken);
        check.Name = request.Name.Trim();
        check.Type = type;
        check.Target = target;
        check.IntervalSeconds = request.IntervalSeconds;
        check.Enabled = request.Enabled;
        check.LastStatus = request.Enabled ? check.LastStatus : ApplicationCheckStatus.Unknown;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "application-check.updated", "ApplicationCheck", check.Id.ToString(), "success",
            new { Previous = previous, Current = new { check.Name, check.Type, check.Target, check.IntervalSeconds, check.Enabled } },
            actor.IpAddress, cancellationToken);
        await _realtime.AgentChangedAsync(actor.TenantId, check.AgentId, cancellationToken);

        var hostname = await _db.Agents.AsNoTracking()
            .Where(a => a.TenantId == actor.TenantId && a.Id == check.AgentId)
            .Select(a => a.Hostname)
            .SingleAsync(cancellationToken);
        return ToDto(check, hostname);
    }

    public async Task<bool> DeleteAsync(RequestActor actor, Guid checkId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(actor.TenantId);
        var check = await _db.ApplicationChecks.SingleOrDefaultAsync(
            c => c.TenantId == actor.TenantId && c.Id == checkId, cancellationToken);
        if (check is null) return false;
        _db.ApplicationChecks.Remove(check);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "application-check.deleted", "ApplicationCheck", check.Id.ToString(), "success",
            new { check.AgentId, check.Name }, actor.IpAddress, cancellationToken);
        await _realtime.AgentChangedAsync(actor.TenantId, check.AgentId, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ApplicationCheckHistoryItemDto>> HistoryAsync(
        Guid tenantId,
        Guid checkId,
        int take,
        CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var exists = await _db.ApplicationChecks.AnyAsync(c => c.TenantId == tenantId && c.Id == checkId, cancellationToken);
        if (!exists) throw new NotFoundException("Application check not found.");
        var rows = await _db.ApplicationCheckResults.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.ApplicationCheckId == checkId)
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ApplicationCheckHistoryItemDto(
            r.EventId, r.Status.ToString(), r.StartedAt, r.FinishedAt,
            r.DurationMilliseconds, r.ErrorCategory, r.ErrorDetail)).ToArray();
    }

    public async Task<HttpCheckTargetPolicyDto> GetHttpCheckTargetPolicyAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var hosts = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.HttpCheckAllowedHosts)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Tenant not found.");
        return new HttpCheckTargetPolicyDto(hosts);
    }

    public async Task<HttpCheckTargetPolicyDto> UpdateHttpCheckTargetPolicyAsync(
        RequestActor actor,
        UpdateHttpCheckTargetPolicyRequest request,
        CancellationToken cancellationToken)
    {
        await _policyValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(actor.TenantId);
        var tenant = await _db.Tenants.SingleOrDefaultAsync(t => t.Id == actor.TenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant not found.");

        var normalized = HttpCheckTargetPolicy.Normalize(request.AllowedHosts);
        if (normalized.Count > 0)
        {
            var existing = await _db.ApplicationChecks.AsNoTracking()
                .Where(c => c.TenantId == actor.TenantId && c.Type == ApplicationCheckType.Http && c.Enabled)
                .Select(c => new { c.Name, c.Target })
                .ToListAsync(cancellationToken);
            var excluded = existing
                .Where(c => !HttpCheckTargetPolicy.IsAllowed(c.Target, normalized))
                .Select(c => c.Name)
                .OrderBy(name => name)
                .ToArray();
            if (excluded.Length > 0)
            {
                var sample = string.Join(", ", excluded.Take(10));
                throw new ConflictException(
                    $"The policy would exclude existing enabled HTTP checks: {sample}"
                    + (excluded.Length > 10 ? ", ..." : string.Empty));
            }
        }

        var previous = tenant.HttpCheckAllowedHosts;
        tenant.HttpCheckAllowedHosts = normalized.ToList();
        tenant.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "application-check.policy.updated", "Tenant", tenant.Id.ToString(), "success",
            new { Previous = previous, Current = tenant.HttpCheckAllowedHosts }, actor.IpAddress, cancellationToken);
        return new HttpCheckTargetPolicyDto(tenant.HttpCheckAllowedHosts);
    }

    private async Task EnsureHttpTargetAllowedAsync(
        Guid tenantId,
        ApplicationCheckType type,
        string target,
        CancellationToken cancellationToken)
    {
        if (type != ApplicationCheckType.Http)
        {
            return;
        }

        var allowedHosts = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.HttpCheckAllowedHosts)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Tenant not found.");
        if (!HttpCheckTargetPolicy.IsAllowed(target, allowedHosts))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateApplicationCheckRequest.Target),
                    "Target host is not allowed by the tenant HTTP check target policy.")
            ]);
        }
    }

    public async Task<IReadOnlyList<AgentApplicationCheckDto>> GetAgentChecksAsync(
        AgentIdentity identity,
        CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(identity.TenantId);
        var active = await _db.Agents.AnyAsync(
            a => a.TenantId == identity.TenantId && a.Id == identity.AgentId && a.Status == AgentStatus.Active,
            cancellationToken);
        if (!active) throw new InvalidCredentialException("Agent is not active.");

        var checks = await _db.ApplicationChecks.AsNoTracking()
            .Where(c => c.TenantId == identity.TenantId && c.AgentId == identity.AgentId && c.Enabled)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);
        return checks.Select(c => new AgentApplicationCheckDto(
            c.Id, c.Name, c.Type.ToString(), c.Target, c.IntervalSeconds)).ToArray();
    }

    public async Task<ApplicationCheckResultsResponse> IngestResultsAsync(
        AgentIdentity identity,
        ApplicationCheckResultsRequest request,
        CancellationToken cancellationToken)
    {
        await _resultsValidator.ValidateAndThrowAsync(request, cancellationToken);
        var results = request.Results ?? Array.Empty<ApplicationCheckResultDto>();
        _tenantContext.SetTenant(identity.TenantId);
        var now = _clock.UtcNow;
        var agentIsActive = await _db.Agents.AnyAsync(
            a => a.TenantId == identity.TenantId && a.Id == identity.AgentId && a.Status == AgentStatus.Active,
            cancellationToken);
        if (!agentIsActive) throw new InvalidCredentialException("Agent is not active.");
        var checkIds = results.Select(r => r.CheckId).Distinct().ToArray();
        var checks = await _db.ApplicationChecks
            .Where(c => c.TenantId == identity.TenantId && c.AgentId == identity.AgentId && c.Enabled && checkIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var eventIds = results.Select(r => r.EventId.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        var existingIds = (await _db.ApplicationCheckResults.AsNoTracking()
                .Where(r => r.TenantId == identity.TenantId && r.AgentId == identity.AgentId && eventIds.Contains(r.EventId))
                .Select(r => r.EventId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var accepted = 0;
        var duplicates = 0;
        var rejected = 0;
        foreach (var result in results)
        {
            var eventId = result.EventId.Trim();
            if (!seenIds.Add(eventId) || existingIds.Contains(eventId))
            {
                duplicates++;
                continue;
            }
            if (!checks.TryGetValue(result.CheckId, out var check)
                || result.StartedAt > now.AddMinutes(5)
                || result.StartedAt < now.AddDays(-1)
                || result.FinishedAt > now.AddMinutes(5))
            {
                rejected++;
                continue;
            }

            var status = Enum.Parse<ApplicationCheckStatus>(result.Status, true);
            if ((check.Type == ApplicationCheckType.Process && status == ApplicationCheckStatus.Healthy)
                || (check.Type != ApplicationCheckType.Process && status == ApplicationCheckStatus.Present))
            {
                rejected++;
                continue;
            }
            var startedAt = result.StartedAt.ToUniversalTime();
            var finishedAt = result.FinishedAt.ToUniversalTime();
            _db.ApplicationCheckResults.Add(new ApplicationCheckResult
            {
                TenantId = identity.TenantId,
                AgentId = identity.AgentId,
                ApplicationCheckId = check.Id,
                EventId = eventId,
                Status = status,
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                DurationMilliseconds = result.DurationMilliseconds,
                ErrorCategory = result.ErrorCategory,
                ErrorDetail = result.ErrorDetail,
                ReceivedAt = now,
                CreatedAt = now
            });
            check.LastExecutedAt = finishedAt;
            check.LastDurationMilliseconds = result.DurationMilliseconds;
            check.LastStatus = status;
            check.LastErrorCategory = result.ErrorCategory;
            check.UpdatedAt = now;
            accepted++;
        }

        if (accepted > 0)
        {
            _db.UsageEvents.Add(new UsageEvent
            {
                TenantId = identity.TenantId,
                AgentId = identity.AgentId,
                Kind = "application_check_result",
                Quantity = accepted,
                OccurredAt = now
            });
            await _db.SaveChangesAsync(cancellationToken);
            await _realtime.TelemetryReceivedAsync(identity.TenantId, identity.AgentId, cancellationToken);
        }
        return new ApplicationCheckResultsResponse(accepted, duplicates, rejected);
    }

    private static ApplicationCheckDto ToDto(ApplicationCheck check, string? hostname = null) =>
        new(check.Id, check.AgentId, hostname ?? string.Empty, check.Name, check.Type.ToString(), check.Target,
            check.IntervalSeconds, check.Enabled, check.LastExecutedAt, check.LastDurationMilliseconds,
            check.LastStatus.ToString(), check.LastErrorCategory);
}
