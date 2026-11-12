namespace Support360.Contracts.Operations;

public sealed record CreateTicketRequest(
    string Type,
    string Title,
    string Description,
    string? Category,
    string? Priority,
    string? Impact,
    string? Urgency,
    Guid? AssignedToUserId,
    IReadOnlyList<Guid>? AlertIds,
    Guid? RelatedBatchJobExecutionId,
    string? Risk,
    string? TargetEnvironment,
    DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedEndAt,
    Guid? RelatedTicketId,
    DateTimeOffset? DueAt = null);

public sealed record SetTicketDueDateRequest(DateTimeOffset? DueAt);

public sealed record TransitionTicketRequest(string Status, Guid? AssignedToUserId, string? Reason);

public sealed record DecideTicketRequest(string Decision, string? Comment);

public sealed record AddTicketCommentRequest(string Body, bool IsInternal);

public sealed record AddTicketWorklogRequest(int Minutes, string? Note);

public sealed record UploadTicketAttachmentRequest(string FileName, string ContentType, byte[] Content);

public static class TicketAttachmentLimits
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public const int MaximumPerTicket = 20;
}

public sealed record TicketAlertDto(Guid AlertId, string Severity, string Status, string Message, DateTimeOffset TriggeredAt);

public sealed record TicketApprovalDto(
    string ApproverDisplay,
    string Decision,
    string? Comment,
    DateTimeOffset DecidedAt);

public sealed record TicketActivityDto(
    string Kind,
    string? FromStatus,
    string? ToStatus,
    string? Detail,
    string? ActorDisplay,
    DateTimeOffset OccurredAt);

public sealed record TicketLinkDto(
    Guid TicketId,
    string Number,
    string Type,
    string LinkType);

public sealed record TicketCommentDto(
    Guid Id,
    string Body,
    bool IsInternal,
    string? AuthorDisplay,
    DateTimeOffset CreatedAt);

public sealed record TicketWorklogDto(
    Guid Id,
    int Minutes,
    string? Note,
    string? AuthorDisplay,
    DateTimeOffset WorkedAt);

public sealed record TicketWatcherDto(
    Guid Id,
    Guid? UserId,
    string Display,
    DateTimeOffset CreatedAt);

public sealed record TicketAttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    string UploadedByDisplay,
    DateTimeOffset UploadedAt);

public sealed record TicketAttachmentContent(string FileName, string ContentType, byte[] Content);

public sealed record TicketDto(
    Guid Id,
    string Number,
    string Type,
    string Title,
    string Description,
    string? Category,
    string Status,
    string Priority,
    string Impact,
    string Urgency,
    Guid? RequestedByUserId,
    string? RequestedByDisplay,
    Guid? AssignedToUserId,
    Guid? RelatedBatchJobExecutionId,
    string? Risk,
    string? TargetEnvironment,
    DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedEndAt,
    int RequiredApprovals,
    DateTimeOffset OpenedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<TicketAlertDto> Alerts,
    IReadOnlyList<TicketApprovalDto> Approvals,
    IReadOnlyList<TicketActivityDto> Activities,
    IReadOnlyList<TicketLinkDto> RelatedTickets,
    IReadOnlyList<TicketCommentDto> Comments,
    IReadOnlyList<TicketWorklogDto> Worklogs,
    IReadOnlyList<TicketWatcherDto> Watchers,
    IReadOnlyList<TicketAttachmentDto> Attachments,
    DateTimeOffset? DueAt);

public sealed record ChangeApprovalPolicyDto(int RequiredApprovals, bool RequireSeparationOfDuties);
public sealed record UpdateChangeApprovalPolicyRequest(int RequiredApprovals, bool RequireSeparationOfDuties);
