using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Support360.Application.Abstractions;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class AlertEvaluationService : IAlertEvaluationService
{
    private const int LookbackMinutes = 10;
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IRealtimeNotifier _realtime;
    private readonly ILogger<AlertEvaluationService> _logger;

    public AlertEvaluationService(
        ISupport360DbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IRealtimeNotifier realtime,
        ILogger<AlertEvaluationService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _clock = clock;
        _realtime = realtime;
        _logger = logger;
    }

    public async Task<int> EvaluateAsync(CancellationToken cancellationToken)
    {
        _tenantContext.SetSystem();
        var now = _clock.UtcNow;
        var since = now.AddMinutes(-LookbackMinutes);

        var rules = await _db.AlertRules
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.Enabled)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            return 0;
        }

        var ruleIds = rules.Select(r => r.Id).ToArray();
        var existingAlerts = await _db.Alerts
            .IgnoreQueryFilters()
            .Where(a => ruleIds.Contains(a.AlertRuleId)
                        && (a.Status == AlertStatus.Active || a.Status == AlertStatus.Acknowledged))
            .ToListAsync(cancellationToken);

        var existingByRuleAndAgent = existingAlerts
            .GroupBy(a => (a.AlertRuleId, a.AgentId))
            .ToDictionary(g => g.Key, g => g.First());

        var agents = await _db.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.Status == AgentStatus.Active)
            .Select(a => new { a.Id, a.TenantId, a.Hostname })
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var metrics = rules.Select(r => r.Metric).Distinct().ToArray();
        var samples = await _db.MetricSamples
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.CapturedAt >= since && metrics.Contains(s.Metric))
            .Select(s => new { s.AgentId, s.TenantId, s.Metric, s.Value, s.CapturedAt })
            .ToListAsync(cancellationToken);

        var samplesByAgentMetric = samples
            .GroupBy(s => (s.AgentId, s.Metric))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.CapturedAt).Take(100).ToArray());

        var changedTenants = new HashSet<Guid>();
        var changed = 0;

        foreach (var rule in rules)
        {
            var agentIds = samplesByAgentMetric.Keys
                .Where(k => k.Metric == rule.Metric)
                .Select(k => k.AgentId)
                .Distinct();

            foreach (var agentId in agentIds)
            {
                if (!agents.TryGetValue(agentId, out var agent) || agent.TenantId != rule.TenantId)
                {
                    continue;
                }

                var recent = samplesByAgentMetric[(agentId, rule.Metric)];
                var required = Math.Max(1, rule.ConsecutiveBreaches);
                var breach = recent.Length >= required && recent.Take(required).All(s => IsBreaching(s.Value, rule));

                if (breach && !existingByRuleAndAgent.ContainsKey((rule.Id, agentId)))
                {
                    var current = recent[0];
                    var alert = new Alert
                    {
                        TenantId = rule.TenantId,
                        AgentId = agentId,
                        AlertRuleId = rule.Id,
                        Status = AlertStatus.Active,
                        Severity = rule.Severity,
                        Message = $"{rule.Name}: {rule.Metric} is {current.Value:0.##} {rule.Operator} {rule.Threshold:0.##} on {agent.Hostname}",
                        TriggeredValue = current.Value,
                        TriggeredAt = now,
                        CreatedAt = now
                    };
                    _db.Alerts.Add(alert);
                    existingByRuleAndAgent[(rule.Id, agentId)] = alert;
                    changedTenants.Add(rule.TenantId);
                    changed++;
                }
                else if (!breach && existingByRuleAndAgent.TryGetValue((rule.Id, agentId), out var activeAlert))
                {
                    activeAlert.Resolve(now);
                    changedTenants.Add(rule.TenantId);
                    changed++;
                }
            }
        }

        if (changed > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var tenantId in changedTenants)
            {
                await _realtime.AlertsChangedAsync(tenantId, cancellationToken);
            }

            _logger.LogInformation("Alert evaluation changed {Changed} alert states", changed);
        }

        return changed;
    }

    private static bool IsBreaching(double value, AlertRule rule) => rule.Operator switch
    {
        ComparisonOperator.GreaterThan => value > rule.Threshold,
        ComparisonOperator.GreaterThanOrEqual => value >= rule.Threshold,
        ComparisonOperator.LessThan => value < rule.Threshold,
        ComparisonOperator.LessThanOrEqual => value <= rule.Threshold,
        _ => false
    };
}
