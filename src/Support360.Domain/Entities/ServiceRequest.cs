using Support360.Domain;

namespace Support360.Domain.Entities;

public sealed class ServiceRequest : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Number { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Category { get; set; }
    public required string RequestedBySubject { get; set; }
    public required string RequestedByDisplay { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.New;
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}
