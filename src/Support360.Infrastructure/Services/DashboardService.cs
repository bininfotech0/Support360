using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Application.Options;
using Support360.Contracts.Dashboard;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IRealtimeNotifier _realtime;
    private readonly AgentPlatformOptions _agentOptions;
    private readonly DeploymentOptions _deploymentOptions;
    private readonly IValidator<CreateAlertRuleRequest> _ruleValidator;

    public DashboardService(
        ISupport360DbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IAuditService audit,
        IRealtimeNotifier realtime,
        IOptions<AgentPlatformOptions> agentOptions,
        IOptions<DeploymentOptions> deploymentOptions,
        IValidator<CreateAlertRuleRequest> ruleValidator)
    {
        _db = db;
        _tenantContext = tenantContext;
        _clock = clock;
        _audit = audit;
        _realtime = realtime;
        _agentOptions = agentOptions.Value;
        _deploymentOptions = deploymentOptions.Value;
        _ruleValidator = ruleValidator;
    }

    public async Task<OverviewDto> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var now = _clock.UtcNow;

        var agents = await _db.Agents.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .Select(a => new { a.Id, a.Status, a.LastHeartbeatAt, a.LastTelemetryAt })
            .ToListAsync(cancellationToken);

        var online = agents.Count(a => a.Status == AgentStatus.Active
                                       && a.LastHeartbeatAt >= now.AddSeconds(-_agentOptions.OfflineAfterSeconds));
        var coverage = agents.Count == 0
            ? 0
            : (int)Math.Round(100.0 * agents.Count(a => a.LastTelemetryAt >= now.AddMinutes(-15)) / agents.Count);

        var activeAlerts = await _db.Alerts.AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId
                             && (a.Status == AlertStatus.Active || a.Status == AlertStatus.Acknowledged), cancellationToken);
        var criticalAlerts = await _db.Alerts.AsNoTracking()
            .CountAsync(a => a.TenantId == tenantId
                             && a.Severity == AlertSeverity.Critical
                             && (a.Status == AlertStatus.Active || a.Status == AlertStatus.Acknowledged), cancellationToken);

        var recentAlertEntities = await _db.Alerts.AsNoTracking()
            .Include(a => a.Agent)
            .Include(a => a.AlertRule)
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.TriggeredAt)
            .Take(5)
            .ToListAsync(cancellationToken);

        var recentEvents = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        return new OverviewDto(
            agents.Count,
            online,
            agents.Count - online,
            activeAlerts,
            criticalAlerts,
            0,
            _deploymentOptions.ExecutionEnabled,
            coverage,
            recentAlertEntities.Select(MapAlert).ToArray(),
            recentEvents.Select(MapAudit).ToArray(),
            now);
    }

    public async Task<IReadOnlyList<AlertDto>> ListAlertsAsync(
        Guid tenantId,
        string? status,
        CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var query = _db.Alerts.AsNoTracking()
            .Include(a => a.Agent)
            .Include(a => a.AlertRule)
            .Where(a => a.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AlertStatus>(status, true, out var parsed))
        {
            query = query.Where(a => a.Status == parsed);
        }

        var alerts = await query
            .OrderByDescending(a => a.TriggeredAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return alerts.Select(MapAlert).ToArray();
    }

    public async Task<bool> AcknowledgeAlertAsync(
        RequestActor actor,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(actor.TenantId);
        var alert = await _db.Alerts.SingleOrDefaultAsync(
            a => a.TenantId == actor.TenantId && a.Id == alertId,
            cancellationToken);
        if (alert is null)
        {
            return false;
        }

        var now = _clock.UtcNow;
        alert.Acknowledge(actor.UserId ?? Guid.Empty, now);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(
            actor.TenantId,
            AuditActorType.User,
            actor.UserId,
            actor.DisplayName,
            "alert.acknowledged",
            "Alert",
            alert.Id.ToString(),
            "success",
            new { alert.Severity, alert.Status },
            actor.IpAddress,
            cancellationToken);
        await _realtime.AlertsChangedAsync(actor.TenantId, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AlertRuleDto>> ListAlertRulesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var rules = await _db.AlertRules.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return rules.Select(MapRule).ToArray();
    }

    public async Task<AlertRuleDto> CreateAlertRuleAsync(
        RequestActor actor,
        CreateAlertRuleRequest request,
        CancellationToken cancellationToken)
    {
        await _ruleValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(actor.TenantId);

        var rule = new AlertRule
        {
            TenantId = actor.TenantId,
            Name = request.Name.Trim(),
            Metric = request.Metric,
            Operator = Enum.Parse<ComparisonOperator>(request.Operator, true),
            Threshold = request.Threshold,
            Severity = Enum.Parse<AlertSeverity>(request.Severity, true),
            ConsecutiveBreaches = request.ConsecutiveBreaches,
            Enabled = true
        };

        _db.AlertRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(
            actor.TenantId,
            AuditActorType.User,
            actor.UserId,
            actor.DisplayName,
            "alert_rule.created",
            "AlertRule",
            rule.Id.ToString(),
            "success",
            new { rule.Name, rule.Metric, rule.Operator, rule.Threshold },
            actor.IpAddress,
            cancellationToken);

        return MapRule(rule);
    }

    public async Task<IReadOnlyList<AuditEventDto>> ListAuditAsync(
        Guid tenantId,
        int take,
        CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var boundedTake = Math.Clamp(take, 1, 200);
        var events = await _db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(boundedTake)
            .ToListAsync(cancellationToken);

        return events.Select(MapAudit).ToArray();
    }

    public async Task<IReadOnlyList<BatchJobDto>> ListBatchJobsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var jobs = await _db.BatchJobs.AsNoTracking()
            .Include(j => j.Agent)
            .Where(j => j.TenantId == tenantId)
            .OrderByDescending(j => j.UpdatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var jobIds = jobs.Select(j => j.Id).ToArray();
        var executions = await _db.BatchJobExecutions.AsNoTracking()
            .Where(e => e.TenantId == tenantId && jobIds.Contains(e.BatchJobId))
            .OrderByDescending(e => e.StartedAt)
            .Take(2000)
            .ToListAsync(cancellationToken);

        var byJob = executions.GroupBy(e => e.BatchJobId).ToDictionary(g => g.Key, g => g.Take(10).ToArray());

        return jobs.Select(job =>
        {
            var runs = byJob.GetValueOrDefault(job.Id) ?? [];
            var last = runs.FirstOrDefault();
            return new BatchJobDto(
                job.Id,
                job.JobKey,
                job.Name,
                job.Scheduler,
                job.ExpectedSchedule,
                job.AgentId,
                job.Agent.Hostname,
                last?.StartedAt,
                last?.Status.ToString(),
                last?.ExitCode,
                runs.Select(MapExecution).ToArray());
        }).ToArray();
    }

    public async Task<UsageSummaryDto> GetUsageAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var since = _clock.UtcNow.AddDays(-30);
        var usage = await _db.UsageEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.OccurredAt >= since)
            .GroupBy(e => e.Kind)
            .Select(g => new { Kind = g.Key, Total = g.Sum(e => e.Quantity) })
            .ToListAsync(cancellationToken);

        long Get(string kind) => usage.FirstOrDefault(u => u.Kind == kind)?.Total ?? 0;

        return new UsageSummaryDto(
            Get("telemetry_sample"),
            Get("heartbeat"),
            Get("job_event"),
            _clock.UtcNow);
    }

    private static AlertDto MapAlert(Alert alert) => new(
        alert.Id,
        alert.AgentId,
        alert.Agent?.Hostname ?? "unknown",
        alert.AlertRuleId,
        alert.AlertRule?.Name ?? "unknown",
        alert.AlertRule?.Metric ?? "unknown",
        alert.Severity.ToString(),
        alert.Status.ToString(),
        alert.Message,
        alert.TriggeredValue,
        alert.TriggeredAt,
        alert.AcknowledgedAt,
        alert.ResolvedAt);

    private static AlertRuleDto MapRule(AlertRule rule) => new(
        rule.Id,
        rule.Name,
        rule.Metric,
        rule.Operator.ToString(),
        rule.Threshold,
        rule.Severity.ToString(),
        rule.ConsecutiveBreaches,
        rule.Enabled);

    private static AuditEventDto MapAudit(AuditEvent e) => new(
        e.Id,
        e.ActorType.ToString(),
        e.ActorDisplay,
        e.Action,
        e.TargetType,
        e.TargetId,
        e.Outcome,
        e.DetailsJson,
        e.IpAddress,
        e.CreatedAt);

    private static BatchJobExecutionDto MapExecution(BatchJobExecution e) => new(
        e.Id,
        e.CorrelationId,
        e.StartedAt,
        e.FinishedAt,
        e.Status.ToString(),
        e.ExitCode,
        e.FailureReason,
        e.FinishedAt is null ? null : Math.Max(0, (e.FinishedAt.Value - e.StartedAt).TotalSeconds),
        e.ReceivedAt);
}
