using Support360.Domain;

namespace Support360.Domain.Entities;

public sealed class Ticket : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public TicketType Type { get; set; }
    public required string Number { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public string? Category { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.New;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public TicketImpact Impact { get; set; } = TicketImpact.Medium;
    public TicketUrgency Urgency { get; set; } = TicketUrgency.Medium;
    public Guid? RequestedByUserId { get; set; }
    public string? RequestedBySubject { get; set; }
    public string? RequestedByDisplay { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? RelatedBatchJobExecutionId { get; set; }
    public string? Risk { get; set; }
    public string? TargetEnvironment { get; set; }
public DateTimeOffset? PlannedStartAt { get; set; }
public DateTimeOffset? PlannedEndAt { get; set; }
public DateTimeOffset? DueAt { get; set; }
    public int RequiredApprovals { get; set; } = 1;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public ICollection<TicketAlert> Alerts { get; set; } = [];
    public ICollection<TicketApproval> Approvals { get; set; } = [];
    public ICollection<TicketActivity> Activities { get; set; } = [];
    public ICollection<TicketComment> Comments { get; set; } = [];
    public ICollection<TicketWorklog> Worklogs { get; set; } = [];
    public ICollection<TicketWatcher> Watchers { get; set; } = [];
    public ICollection<TicketAttachment> Attachments { get; set; } = [];
}

public sealed class TicketAlert : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public Guid AlertId { get; set; }
    public Alert Alert { get; set; } = null!;
    public DateTimeOffset LinkedAt { get; set; }
    public Guid? LinkedByUserId { get; set; }
}

public sealed class TicketApproval : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public required string ApproverSubject { get; set; }
    public Guid? ApproverUserId { get; set; }
    public required string ApproverDisplay { get; set; }
    public TicketApprovalDecision Decision { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}

public sealed class TicketActivity : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public required string Kind { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Detail { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorDisplay { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class TicketLink : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid SourceTicketId { get; set; }
    public Ticket SourceTicket { get; set; } = null!;
    public Guid TargetTicketId { get; set; }
    public Ticket TargetTicket { get; set; } = null!;
    public TicketLinkType LinkType { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}

public sealed class TicketComment : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public Guid? AuthorUserId { get; set; }
    public string? AuthorSubject { get; set; }
    public required string AuthorDisplay { get; set; }
    public required string Body { get; set; }
    public bool IsInternal { get; set; }
}

public sealed class TicketWorklog : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public Guid? AuthorUserId { get; set; }
    public string? AuthorSubject { get; set; }
    public required string AuthorDisplay { get; set; }
    public int Minutes { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset WorkedAt { get; set; }
}

public sealed class TicketWatcher : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public Guid? UserId { get; set; }
    public required string Subject { get; set; }
    public required string Display { get; set; }
}

public sealed class TicketAttachment : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string Sha256 { get; set; }
    public required byte[] Content { get; set; }
    public Guid? UploadedByUserId { get; set; }
    public required string UploadedByDisplay { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class TicketNumberSequence : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public TicketType Type { get; set; }
    public int LastIssued { get; set; }
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
}
