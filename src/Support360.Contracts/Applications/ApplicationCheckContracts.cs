namespace Support360.Contracts.Applications;

public sealed record CreateApplicationCheckRequest(
    Guid AgentId,
    string Name,
    string Type,
    string Target,
    int IntervalSeconds,
    bool Enabled = true);

public sealed record UpdateApplicationCheckRequest(
    string Name,
    string Type,
    string Target,
    int IntervalSeconds,
    bool Enabled);

public sealed record ApplicationCheckDto(
    Guid Id,
    Guid AgentId,
    string Hostname,
    string Name,
    string Type,
    string Target,
    int IntervalSeconds,
    bool Enabled,
    DateTimeOffset? LastExecutedAt,
    long? LastDurationMilliseconds,
    string LastStatus,
    string? LastErrorCategory);

public sealed record ApplicationCheckHistoryItemDto(
    string EventId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    long DurationMilliseconds,
    string? ErrorCategory,
    string? ErrorDetail);

public sealed record AgentApplicationCheckDto(
    Guid Id,
    string Name,
    string Type,
    string Target,
    int IntervalSeconds);

public sealed record ApplicationCheckResultDto(
    Guid CheckId,
    string EventId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    long DurationMilliseconds,
    string? ErrorCategory,
    string? ErrorDetail);

public sealed record ApplicationCheckResultsRequest(IReadOnlyList<ApplicationCheckResultDto>? Results);

public sealed record ApplicationCheckResultsResponse(int Accepted, int Duplicates, int Rejected);

public sealed record HttpCheckTargetPolicyDto(IReadOnlyList<string> AllowedHosts);

public sealed record UpdateHttpCheckTargetPolicyRequest(IReadOnlyList<string>? AllowedHosts);
