namespace Support360.Domain.Entities;

public sealed class BatchJob : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    public required string JobKey { get; set; }
    public required string Name { get; set; }
    public string? Scheduler { get; set; }
    public string? ExpectedSchedule { get; set; }
    public bool IsConfigured { get; set; }
    public ICollection<BatchJobExecution> Executions { get; set; } = [];
}
