namespace Support360.Domain.Entities;

public sealed class AlertRule : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Metric { get; set; }
    public ComparisonOperator Operator { get; set; }
    public double Threshold { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public int ConsecutiveBreaches { get; set; } = 1;
    public bool Enabled { get; set; } = true;
}

public sealed class Alert : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    public Guid AlertRuleId { get; set; }
    public AlertRule AlertRule { get; set; } = null!;
    public AlertStatus Status { get; set; } = AlertStatus.Active;
    public AlertSeverity Severity { get; set; }
    public required string Message { get; set; }
    public double TriggeredValue { get; set; }
    public DateTimeOffset TriggeredAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    public void Acknowledge(Guid userId, DateTimeOffset now)
    {
        if (Status == AlertStatus.Active)
        {
            Status = AlertStatus.Acknowledged;
            AcknowledgedAt = now;
            AcknowledgedByUserId = userId;
            UpdatedAt = now;
        }
    }

    public void Resolve(DateTimeOffset now)
    {
        if (Status != AlertStatus.Resolved)
        {
            Status = AlertStatus.Resolved;
            ResolvedAt = now;
            UpdatedAt = now;
        }
    }
}
