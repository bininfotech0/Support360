namespace Support360.Domain.Entities;

public sealed class AgentEnrollment : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public required string TokenHash { get; set; }
    public required string TokenPrefix { get; set; }
    public Guid CreatedByUserId { get; set; }
    public AgentPlatform? TargetPlatform { get; set; }
    public bool AllowDeploymentExecution { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Guid? ConsumedByAgentId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) =>
        ConsumedAt is null && RevokedAt is null && ExpiresAt > now;
}
