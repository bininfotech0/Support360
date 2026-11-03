using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Application.Options;
using Support360.Application.Security;
using Support360.Contracts.Agents;
using Support360.Contracts.Telemetry;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class TelemetryIngestionService : ITelemetryIngestionService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IRealtimeNotifier _realtime;
    private readonly AgentPlatformOptions _options;
    private readonly DeploymentOptions _deploymentOptions;
    private readonly IValidator<HeartbeatRequest> _heartbeatValidator;
    private readonly IValidator<TelemetryBatchRequest> _telemetryValidator;
    private readonly IValidator<JobEventBatchRequest> _jobValidator;
    private readonly ILogger<TelemetryIngestionService> _logger;

    public TelemetryIngestionService(
        ISupport360DbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IRealtimeNotifier realtime,
        IOptions<AgentPlatformOptions> options,
        IOptions<DeploymentOptions> deploymentOptions,
        IValidator<HeartbeatRequest> heartbeatValidator,
        IValidator<TelemetryBatchRequest> telemetryValidator,
        IValidator<JobEventBatchRequest> jobValidator,
        ILogger<TelemetryIngestionService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _clock = clock;
        _realtime = realtime;
        _options = options.Value;
        _deploymentOptions = deploymentOptions.Value;
        _heartbeatValidator = heartbeatValidator;
        _telemetryValidator = telemetryValidator;
        _jobValidator = jobValidator;
        _logger = logger;
    }

    public async Task<HeartbeatResponse> HeartbeatAsync(
        AgentIdentity identity,
        HeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        await _heartbeatValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(identity.TenantId);

        var now = _clock.UtcNow;
        var agent = await _db.Agents
            .Include(a => a.Credentials)
            .SingleOrDefaultAsync(a => a.TenantId == identity.TenantId && a.Id == identity.AgentId, cancellationToken)
            ?? throw new InvalidCredentialException("Agent is not registered.");

        if (agent.Status != AgentStatus.Active)
        {
            throw new InvalidCredentialException("Agent is not active.");
        }

        var capabilitiesJson = request.Capabilities is null
            ? agent.CapabilitiesJson
            : JsonSerializer.Serialize(request.Capabilities);

        agent.RecordHeartbeat(
            now,
            request.Version,
            request.OsVersion,
            request.Architecture,
            capabilitiesJson,
            request.CollectorHealthy,
            request.CollectorHealth,
            request.UptimeSeconds);

        if (request.Services is not null)
        {
            var existing = await _db.AgentServiceStatuses
                .Where(s => s.TenantId == identity.TenantId && s.AgentId == identity.AgentId)
                .ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);

            foreach (var service in request.Services.Take(200))
            {
                var state = Enum.TryParse<ServiceRunState>(service.State, true, out var parsed)
                    ? parsed
                    : ServiceRunState.Unknown;
                var checkedAt = NormalizeClientTime(service.CheckedAt, now);

                if (existing.TryGetValue(service.Name, out var row))
                {
                    row.Kind = service.Kind;
                    row.State = state;
                    row.CheckedAt = checkedAt;
                    row.Detail = service.Detail;
                    row.UpdatedAt = now;
                }
                else
                {
                    _db.AgentServiceStatuses.Add(new AgentServiceStatus
                    {
                        TenantId = identity.TenantId,
                        AgentId = identity.AgentId,
                        Name = service.Name,
                        Kind = service.Kind,
                        State = state,
                        CheckedAt = checkedAt,
                        Detail = service.Detail,
                        CreatedAt = now
                    });
                }
            }
        }

        _db.UsageEvents.Add(new UsageEvent
        {
            TenantId = identity.TenantId,
            AgentId = identity.AgentId,
            Kind = "heartbeat",
            Quantity = 1,
            OccurredAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        await _realtime.AgentChangedAsync(identity.TenantId, identity.AgentId, cancellationToken);

        return new HeartbeatResponse(
            now,
            _options.HeartbeatIntervalSeconds,
            _options.TelemetryIntervalSeconds,
            _deploymentOptions.ExecutionEnabled,
            MetricCatalog.Allowed);
    }

    public async Task<TelemetryIngestResponse> IngestTelemetryAsync(
        AgentIdentity identity,
        TelemetryBatchRequest request,
        CancellationToken cancellationToken)
    {
        await _telemetryValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(identity.TenantId);

        if (request.Samples is null || request.Samples.Count == 0)
        {
            return new TelemetryIngestResponse(0, 0, 0, []);
        }

        if (request.Samples.Count > _options.MaxMetricsPerBatch)
        {
            throw new FluentValidation.ValidationException(
                $"A telemetry batch may contain at most {_options.MaxMetricsPerBatch} samples.");
        }

        var now = _clock.UtcNow;
        var agentExists = await _db.Agents.AnyAsync(
            a => a.TenantId == identity.TenantId && a.Id == identity.AgentId && a.Status == AgentStatus.Active,
            cancellationToken);
        if (!agentExists)
        {
            throw new InvalidCredentialException("Agent is not active.");
        }

        var rejectedReasons = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var valid = new List<MetricSample>();

        foreach (var sample in request.Samples)
        {
            if (!seen.Add(sample.EventId))
            {
                continue;
            }

            var reason = ValidateMetric(sample, now);
            if (reason is not null)
            {
                if (rejectedReasons.Count < 20)
                {
                    rejectedReasons.Add($"{sample.EventId}: {reason}");
                }

                continue;
            }

            valid.Add(new MetricSample
            {
                TenantId = identity.TenantId,
                AgentId = identity.AgentId,
                EventId = sample.EventId.Trim(),
                Metric = sample.Metric.Trim(),
                Value = sample.Value,
                Unit = sample.Unit.Trim(),
                CapturedAt = sample.CapturedAt.ToUniversalTime(),
                ReceivedAt = now,
                CreatedAt = now
            });
        }

        var eventIds = valid.Select(s => s.EventId).ToArray();
        var existing = eventIds.Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : (await _db.MetricSamples
                .AsNoTracking()
                .Where(s => s.TenantId == identity.TenantId && s.AgentId == identity.AgentId && eventIds.Contains(s.EventId))
                .Select(s => s.EventId)
                .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

        var toInsert = valid.Where(s => !existing.Contains(s.EventId)).ToArray();
        var withinBatchDuplicates = request.Samples.Count - seen.Count;
        var duplicateCount = withinBatchDuplicates + (valid.Count - toInsert.Length);
        var rejected = request.Samples.Count - withinBatchDuplicates - valid.Count;

        if (toInsert.Length > 0)
        {
            _db.MetricSamples.AddRange(toInsert);
            var agent = await _db.Agents.SingleAsync(
                a => a.TenantId == identity.TenantId && a.Id == identity.AgentId,
                cancellationToken);
            agent.LastTelemetryAt = now;
            agent.UpdatedAt = now;

            _db.UsageEvents.Add(new UsageEvent
            {
                TenantId = identity.TenantId,
                AgentId = identity.AgentId,
                Kind = "telemetry_sample",
                Quantity = toInsert.Length,
                OccurredAt = now
            });
        }

        if (toInsert.Length > 0 || duplicateCount > 0 || rejected > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (toInsert.Length > 0)
        {
            await _realtime.TelemetryReceivedAsync(identity.TenantId, identity.AgentId, cancellationToken);
            _logger.LogInformation(
                "Ingested {Accepted} telemetry samples for agent {AgentId}; {Duplicates} duplicates, {Rejected} rejected",
                toInsert.Length, identity.AgentId, duplicateCount, rejected);
        }

        return new TelemetryIngestResponse(toInsert.Length, duplicateCount, rejected, rejectedReasons);
    }

    public async Task<JobEventsResponse> IngestJobEventsAsync(
        AgentIdentity identity,
        JobEventBatchRequest request,
        CancellationToken cancellationToken)
    {
        await _jobValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(identity.TenantId);

        if (request.Events is null || request.Events.Count == 0)
        {
            return new JobEventsResponse(0, 0, 0);
        }

        if (request.Events.Count > _options.MaxJobEventsPerBatch)
        {
            throw new FluentValidation.ValidationException(
                $"A job event batch may contain at most {_options.MaxJobEventsPerBatch} events.");
        }

        var now = _clock.UtcNow;
        var agent = await _db.Agents.SingleOrDefaultAsync(
            a => a.TenantId == identity.TenantId && a.Id == identity.AgentId && a.Status == AgentStatus.Active,
            cancellationToken) ?? throw new InvalidCredentialException("Agent is not active.");

        var correlationIds = request.Events.Select(e => e.CorrelationId.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        var existing = (await _db.BatchJobExecutions
                .AsNoTracking()
                .Where(e => e.TenantId == identity.TenantId && e.AgentId == identity.AgentId && correlationIds.Contains(e.CorrelationId))
                .Select(e => e.CorrelationId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var jobs = await _db.BatchJobs
            .Where(j => j.TenantId == identity.TenantId && j.AgentId == identity.AgentId)
            .ToDictionaryAsync(j => j.JobKey, StringComparer.Ordinal, cancellationToken);

        var accepted = 0;
        var duplicates = 0;
        var rejected = 0;

        foreach (var jobEvent in request.Events)
        {
            var correlationId = jobEvent.CorrelationId.Trim();
            if (!existing.Add(correlationId))
            {
                duplicates++;
                continue;
            }

            if (!Enum.TryParse<BatchExecutionStatus>(jobEvent.Status, true, out var status)
                || jobEvent.StartedAt > now.AddMinutes(_options.MaxClockSkewMinutes)
                || jobEvent.StartedAt < now.AddDays(-31)
                || (jobEvent.FinishedAt is not null && jobEvent.FinishedAt > now.AddMinutes(_options.MaxClockSkewMinutes)))
            {
                rejected++;
                continue;
            }

            if (!jobs.TryGetValue(jobEvent.JobKey.Trim(), out var job))
            {
                job = new BatchJob
                {
                    TenantId = identity.TenantId,
                    AgentId = identity.AgentId,
                    JobKey = jobEvent.JobKey.Trim(),
                    Name = jobEvent.Name.Trim(),
                    Scheduler = jobEvent.Scheduler,
                    CreatedAt = now
                };
                jobs[job.JobKey] = job;
                _db.BatchJobs.Add(job);
            }
            else if (!job.IsConfigured)
            {
                job.Name = jobEvent.Name.Trim();
                job.Scheduler = jobEvent.Scheduler;
                job.UpdatedAt = now;
            }

            _db.BatchJobExecutions.Add(new BatchJobExecution
            {
                TenantId = identity.TenantId,
                AgentId = identity.AgentId,
                BatchJobId = job.Id,
                CorrelationId = correlationId,
                StartedAt = jobEvent.StartedAt.ToUniversalTime(),
                FinishedAt = jobEvent.FinishedAt?.ToUniversalTime(),
                Status = status,
                ExitCode = jobEvent.ExitCode,
                FailureReason = jobEvent.FailureReason,
                ReceivedAt = now,
                CreatedAt = now
            });

            accepted++;
        }

        if (accepted > 0)
        {
            _db.UsageEvents.Add(new UsageEvent
            {
                TenantId = identity.TenantId,
                AgentId = identity.AgentId,
                Kind = "job_event",
                Quantity = accepted,
                OccurredAt = now
            });

            agent.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken);
            await _realtime.TelemetryReceivedAsync(identity.TenantId, identity.AgentId, cancellationToken);
        }

        return new JobEventsResponse(accepted, duplicates, rejected);
    }

    private string? ValidateMetric(MetricSampleDto sample, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sample.EventId) || sample.EventId.Length > 100)
        {
            return "invalid event id";
        }

        if (!MetricCatalog.Units.TryGetValue(sample.Metric, out var expectedUnit))
        {
            return "metric not allowed";
        }

        if (!string.Equals(sample.Unit, expectedUnit, StringComparison.Ordinal))
        {
            return $"unit must be '{expectedUnit}'";
        }

        if (!double.IsFinite(sample.Value))
        {
            return "value must be finite";
        }

        if (sample.CapturedAt > now.AddMinutes(_options.MaxClockSkewMinutes))
        {
            return "timestamp is in the future";
        }

        if (sample.CapturedAt < now.AddHours(-_options.MaxMetricAgeHours))
        {
            return "timestamp is too old";
        }

        return null;
    }

    private static DateTimeOffset NormalizeClientTime(DateTimeOffset value, DateTimeOffset now)
    {
        if (value > now.AddMinutes(5) || value < now.AddDays(-1))
        {
            return now;
        }

        return value.ToUniversalTime();
    }
}
