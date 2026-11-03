namespace Support360.Domain.Entities;

public sealed class ApplicationCheck : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public required string Name { get; set; }
    public ApplicationCheckType Type { get; set; }
    public required string Target { get; set; }
    public int IntervalSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastExecutedAt { get; set; }
    public long? LastDurationMilliseconds { get; set; }
    public ApplicationCheckStatus LastStatus { get; set; } = ApplicationCheckStatus.Unknown;
    public string? LastErrorCategory { get; set; }
    public ICollection<ApplicationCheckResult> Results { get; set; } = [];
}
