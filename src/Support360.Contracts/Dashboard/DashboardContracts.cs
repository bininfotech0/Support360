using Support360.Contracts.Agents;

namespace Support360.Contracts.Dashboard;

public sealed record OverviewDto(
    int TotalAgents,
    int OnlineAgents,
    int OfflineAgents,
    int ActiveAlerts,
    int CriticalAlerts,
    int ActiveDeployments,
    bool DeploymentExecutionEnabled,
    int MonitoringCoveragePercent,
    IReadOnlyList<AlertDto> RecentAlerts,
    IReadOnlyList<AuditEventDto> RecentEvents,
    DateTimeOffset GeneratedAt);

public sealed record AlertDto(
    Guid Id,
    Guid AgentId,
    string Hostname,
    Guid AlertRuleId,
    string RuleName,
    string Metric,
    string Severity,
    string Status,
    string Message,
    double TriggeredValue,
    DateTimeOffset TriggeredAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt);

public sealed record AlertRuleDto(
    Guid Id,
    string Name,
    string Metric,
    string Operator,
    double Threshold,
    string Severity,
    int ConsecutiveBreaches,
    bool Enabled);

public sealed record CreateAlertRuleRequest(
    string Name,
    string Metric,
    string Operator,
    double Threshold,
    string Severity,
    int ConsecutiveBreaches);

public sealed record AuditEventDto(
    Guid Id,
    string ActorType,
    string? ActorDisplay,
    string Action,
    string TargetType,
    string? TargetId,
    string Outcome,
    string? DetailsJson,
    string? IpAddress,
    DateTimeOffset CreatedAt);

public sealed record BatchJobDto(
    Guid Id,
    string JobKey,
    string Name,
    string? Scheduler,
    string? ExpectedSchedule,
    Guid AgentId,
    string Hostname,
    DateTimeOffset? LastRunAt,
    string? LastStatus,
    int? LastExitCode,
    IReadOnlyList<BatchJobExecutionDto> RecentExecutions);

public sealed record BatchJobExecutionDto(
    Guid Id,
    string CorrelationId,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string Status,
    int? ExitCode,
    string? FailureReason,
    double? DurationSeconds,
    DateTimeOffset ReceivedAt);

public sealed record UsageSummaryDto(
    long TelemetrySamplesLast30Days,
    long AgentHeartbeatsLast30Days,
    long JobEventsLast30Days,
    DateTimeOffset GeneratedAt);

public sealed record AgentLivenessDto(Guid AgentId, string Hostname, string Connectivity, DateTimeOffset? LastHeartbeatAt);
