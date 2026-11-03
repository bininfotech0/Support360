using Support360.Contracts.Agents;

namespace Support360.Contracts.Telemetry;

public sealed record HeartbeatRequest(
    string? Version,
    string? OsVersion,
    string? Architecture,
    long? UptimeSeconds,
    bool CollectorHealthy,
    string? CollectorHealth,
    IReadOnlyList<string>? Capabilities,
    IReadOnlyList<ServiceStatusDto>? Services);

public sealed record HeartbeatResponse(
    DateTimeOffset ServerTime,
    int HeartbeatIntervalSeconds,
    int TelemetryIntervalSeconds,
    bool DeploymentExecutionEnabled,
    IReadOnlyList<string> AllowedMetrics);

public sealed record MetricSampleDto(
    string EventId,
    string Metric,
    double Value,
    string Unit,
    DateTimeOffset CapturedAt);

public sealed record TelemetryBatchRequest(IReadOnlyList<MetricSampleDto>? Samples);

public sealed record TelemetryIngestResponse(
    int Accepted,
    int Duplicates,
    int Rejected,
    IReadOnlyList<string> RejectedReasons);

public sealed record JobEventDto(
    string CorrelationId,
    string JobKey,
    string Name,
    string? Scheduler,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Status,
    int? ExitCode,
    string? FailureReason);

public sealed record JobEventBatchRequest(IReadOnlyList<JobEventDto>? Events);

public sealed record JobEventsResponse(int Accepted, int Duplicates, int Rejected);

public sealed record AgentConfigResponse(
    int HeartbeatIntervalSeconds,
    int TelemetryIntervalSeconds,
    IReadOnlyList<string> AllowedMetrics,
    bool DeploymentExecutionEnabled,
    DateTimeOffset ServerTime);
