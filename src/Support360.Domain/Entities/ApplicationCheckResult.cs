namespace Support360.Domain.Entities;

public sealed class ApplicationCheckResult : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Guid ApplicationCheckId { get; set; }
    public ApplicationCheck ApplicationCheck { get; set; } = null!;
    public required string EventId { get; set; }
    public ApplicationCheckStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public long DurationMilliseconds { get; set; }
    public string? ErrorCategory { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
