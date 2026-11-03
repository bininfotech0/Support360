namespace Support360.Domain.Entities;

public sealed class AuditEvent : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public AuditActorType ActorType { get; set; }
    public Guid? ActorId { get; set; }
    public string? ActorDisplay { get; set; }
    public required string Action { get; set; }
    public required string TargetType { get; set; }
    public string? TargetId { get; set; }
    public required string Outcome { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
}
