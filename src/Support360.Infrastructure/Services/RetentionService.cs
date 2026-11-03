using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Application.Options;
using Support360.Domain;

namespace Support360.Infrastructure.Services;

public sealed class RetentionService : IRetentionService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly RetentionOptions _options;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(
        ISupport360DbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IOptions<RetentionOptions> options,
        ILogger<RetentionService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> ApplyAsync(CancellationToken cancellationToken)
    {
        _tenantContext.SetSystem();
        var now = _clock.UtcNow;

        var samples = await _db.MetricSamples
            .IgnoreQueryFilters()
            .Where(s => s.CapturedAt < now.AddDays(-_options.TelemetryDays))
            .ExecuteDeleteAsync(cancellationToken);

        var audits = await _db.AuditEvents
            .IgnoreQueryFilters()
            .Where(a => a.CreatedAt < now.AddDays(-_options.AuditDays))
            .ExecuteDeleteAsync(cancellationToken);

        var alerts = await _db.Alerts
            .IgnoreQueryFilters()
            .Where(a => a.Status == AlertStatus.Resolved
                        && a.ResolvedAt != null
                        && a.ResolvedAt < now.AddDays(-_options.ResolvedAlertDays))
            .ExecuteDeleteAsync(cancellationToken);

        var usage = await _db.UsageEvents
            .IgnoreQueryFilters()
            .Where(u => u.OccurredAt < now.AddDays(-_options.UsageDays))
            .ExecuteDeleteAsync(cancellationToken);

        var checkResults = await _db.ApplicationCheckResults
            .IgnoreQueryFilters()
            .Where(r => r.ReceivedAt < now.AddDays(-_options.ApplicationCheckResultDays))
            .ExecuteDeleteAsync(cancellationToken);

        var total = samples + audits + alerts + usage + checkResults;
        if (total > 0)
        {
            _logger.LogInformation(
                "Retention removed {Samples} samples, {Audits} audit rows, {Alerts} alerts, {Usage} usage rows and {CheckResults} application check results",
                samples, audits, alerts, usage, checkResults);
        }

        return total;
    }
}
