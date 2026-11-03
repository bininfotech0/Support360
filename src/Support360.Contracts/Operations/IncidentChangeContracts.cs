namespace Support360.Contracts.Operations;

public sealed record CreateIncidentRequest(
    string Title,
    string Description,
    string Severity,
    IReadOnlyList<Guid>? AlertIds,
    Guid? RelatedBatchJobExecutionId);

public sealed record TransitionIncidentRequest(string Status, Guid? AssignedToUserId);

public sealed record IncidentAlertDto(Guid AlertId, string Severity, string Status, string Message, DateTimeOffset TriggeredAt);

public sealed record IncidentDto(
    Guid Id,
    string Number,
    string Title,
    string Description,
    string Severity,
    string Status,
    Guid? CreatedByUserId,
    Guid? AssignedToUserId,
    Guid? RelatedBatchJobExecutionId,
    DateTimeOffset OpenedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<IncidentAlertDto> Alerts,
    IReadOnlyList<Guid> RelatedChangeRequestIds);

public sealed record CreateChangeRequest(
    string Title,
    string Description,
    string Risk,
    string TargetEnvironment,
    DateTimeOffset PlannedStartAt,
    DateTimeOffset PlannedEndAt,
    Guid? RelatedIncidentId);

public sealed record DecideChangeRequest(string Decision, string? Comment);

public sealed record ChangeApprovalDto(
    string ApproverDisplay,
    string Decision,
    string? Comment,
    DateTimeOffset DecidedAt);

public sealed record ChangeRequestDto(
    Guid Id,
    string Number,
    string Title,
    string Description,
    string Risk,
    string TargetEnvironment,
    DateTimeOffset PlannedStartAt,
    DateTimeOffset PlannedEndAt,
    int RequiredApprovals,
    string Status,
    Guid? RequestedByUserId,
    string RequestedByDisplay,
    Guid? RelatedIncidentId,
    IReadOnlyList<ChangeApprovalDto> Approvals);

public sealed record ChangeApprovalPolicyDto(int RequiredApprovals, bool RequireSeparationOfDuties);
public sealed record UpdateChangeApprovalPolicyRequest(int RequiredApprovals, bool RequireSeparationOfDuties);
