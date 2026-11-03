using Support360.Domain;

namespace Support360.Domain.Entities;

public sealed class Incident : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Number { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public IncidentSeverity Severity { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.New;
    public Guid? CreatedByUserId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? RelatedBatchJobExecutionId { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public ICollection<IncidentAlert> Alerts { get; set; } = [];
    public ICollection<ChangeRequest> ChangeRequests { get; set; } = [];
}

public sealed class IncidentAlert : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public Guid AlertId { get; set; }
    public Alert Alert { get; set; } = null!;
    public DateTimeOffset LinkedAt { get; set; }
    public Guid? LinkedByUserId { get; set; }
}
