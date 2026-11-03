namespace Support360.Domain.Entities;

public sealed class MetricSample : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    public required string EventId { get; set; }
    public required string Metric { get; set; }
    public double Value { get; set; }
    public required string Unit { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
}
