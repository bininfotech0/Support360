using Support360.Domain;

namespace Support360.Domain.Entities;

public sealed class ChangeRequest : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Number { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Risk { get; set; }
    public required string TargetEnvironment { get; set; }
    public DateTimeOffset PlannedStartAt { get; set; }
    public DateTimeOffset PlannedEndAt { get; set; }
    public int RequiredApprovals { get; set; }
    public ChangeRequestStatus Status { get; set; } = ChangeRequestStatus.PendingApproval;
    public Guid? RequestedByUserId { get; set; }
    public required string RequestedBySubject { get; set; }
    public required string RequestedByDisplay { get; set; }
    public Guid ConcurrencyToken { get; set; }
    public Guid? RelatedIncidentId { get; set; }
    public Incident? RelatedIncident { get; set; }
    public ICollection<ChangeApproval> Approvals { get; set; } = [];
}

public sealed class ChangeApproval : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ChangeRequestId { get; set; }
    public ChangeRequest ChangeRequest { get; set; } = null!;
    public required string ApproverSubject { get; set; }
    public Guid? ApproverUserId { get; set; }
    public required string ApproverDisplay { get; set; }
    public ChangeApprovalDecision Decision { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
}
