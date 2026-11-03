namespace Support360.Domain.Entities;

public sealed class AgentServiceStatus : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public ServiceRunState State { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public string? Detail { get; set; }
}
