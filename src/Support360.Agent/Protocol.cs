namespace Support360.Agent;

public static class AgentJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);
}

public sealed record EnrollRequest(
    string Token,
    string Hostname,
    string Platform,
    string OsVersion,
    string Architecture,
    string AgentVersion,
    IReadOnlyList<string> Capabilities);

public sealed record EnrollResponse(
    string AgentId,
    string TenantId,
    string ApiKey,
    int HeartbeatIntervalSeconds,
    int TelemetryIntervalSeconds,
    IReadOnlyList<string> AllowedMetrics,
    DateTimeOffset ServerTime);

public sealed record HeartbeatRequest(
    string Version,
    string OsVersion,
    string Architecture,
    long UptimeSeconds,
    bool CollectorHealthy,
    string CollectorHealth,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<object> Services);

public sealed record MetricSample(
    string EventId,
    string Metric,
    double Value,
    string Unit,
    DateTimeOffset CapturedAt);

public sealed record TelemetryResponse(int Accepted, int Duplicates, int Rejected);

public sealed record AgentConfig(
    int HeartbeatIntervalSeconds,
    int TelemetryIntervalSeconds,
    IReadOnlyList<string> AllowedMetrics);

public sealed record ApplicationCheck(
    string Id,
    string Name,
    string Type,
    string Target,
    int IntervalSeconds);

public sealed record ApplicationCheckResult(
    string CheckId,
    string EventId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    long DurationMilliseconds,
    string? ErrorCategory,
    string? ErrorDetail);

public sealed record ApplicationCheckResultsResponse(int Accepted, int Duplicates, int Rejected);

public sealed record DeploymentRequest(
    string Id,
    string PackageName,
    string PackageVersion,
    string PackageSha256,
    string? GitRepoUrl,
    string? GitBranch,
    string? TargetPath,
    string Environment);

public sealed record DeploymentManifest(
    string DeploymentId,
    string AgentId,
    string TenantId,
    string PackageName,
    string PackageVersion,
    string PackageSha256,
    string? GitRepoUrl,
    string? GitBranch,
    string? TargetPath,
    string Nonce,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string SignatureAlgorithm,
    string KeyId,
    string Signature);

public sealed record DeploymentResult(string DeploymentId, string Status, string? Detail);
