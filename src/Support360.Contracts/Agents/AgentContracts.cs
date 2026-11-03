namespace Support360.Contracts.Agents;

public sealed record AgentSummaryDto(
    Guid Id,
    string Hostname,
    string Platform,
    string OsVersion,
    string Architecture,
    string Version,
    string Status,
    string Connectivity,
    DateTimeOffset EnrolledAt,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset? LastTelemetryAt,
    bool CollectorHealthy,
    long? UptimeSeconds,
    double? CpuPercent,
    double? MemoryPercent,
    double? DiskPercent,
    int ActiveAlerts);

public sealed record ServiceStatusDto(
    string Name,
    string Kind,
    string State,
    DateTimeOffset CheckedAt,
    string? Detail);

public sealed record AgentDetailResponse(
    AgentSummaryDto Agent,
    IReadOnlyList<ServiceStatusDto> Services,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<AgentCredentialDto> Credentials,
    bool DeploymentExecutionAllowed = false);

public sealed record AgentCredentialDto(
    Guid Id,
    string KeyPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    bool IsExpired);

public sealed record CreateEnrollmentRequest(string? TargetPlatform, bool AllowDeploymentExecution = false);

public sealed record EnrollmentCreatedResponse(
    Guid EnrollmentId,
    string Token,
    string TokenPrefix,
    AgentPlatformDto? TargetPlatform,
    DateTimeOffset ExpiresAt,
    string EnrollCommand,
    bool AllowDeploymentExecution = false);

public sealed record AgentPlatformDto(string Platform);

public sealed record EnrollmentListItemDto(
    Guid Id,
    string TokenPrefix,
    string? TargetPlatform,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    Guid? ConsumedByAgentId,
    bool IsUsable,
    bool AllowDeploymentExecution = false);

public sealed record EnrollAgentRequest(
    string Token,
    string Hostname,
    string Platform,
    string? OsVersion,
    string? Architecture,
    string? AgentVersion,
    IReadOnlyList<string>? Capabilities);

public sealed record EnrollAgentResponse(
    Guid AgentId,
    Guid TenantId,
    string ApiKey,
    string ApiKeyPrefix,
    int HeartbeatIntervalSeconds,
    int TelemetryIntervalSeconds,
    IReadOnlyList<string> AllowedMetrics,
    DateTimeOffset ServerTime);

public sealed record RotateCredentialResponse(
    Guid CredentialId,
    string ApiKey,
    string ApiKeyPrefix,
    DateTimeOffset CreatedAt);

public sealed record RevokeAgentResponse(Guid AgentId, string Status, DateTimeOffset RevokedAt);

public sealed record AgentMetricPointDto(DateTimeOffset Timestamp, double Value);

public sealed record AgentMetricsResponse(
    Guid AgentId,
    string Range,
    IReadOnlyDictionary<string, IReadOnlyList<AgentMetricPointDto>> Series,
    IReadOnlyDictionary<string, double> Latest);
