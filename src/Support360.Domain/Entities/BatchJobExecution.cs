namespace Support360.Domain.Entities;

public sealed class BatchJobExecution : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Guid BatchJobId { get; set; }
    public BatchJob BatchJob { get; set; } = null!;
    public required string CorrelationId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public BatchExecutionStatus Status { get; set; }
    public int? ExitCode { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
