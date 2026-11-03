namespace Support360.Domain.Entities;

public sealed class UsageEvent : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid? AgentId { get; set; }
    public required string Kind { get; set; }
    public long Quantity { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
