namespace Support360.Domain.Entities;

public sealed class AgentCredential : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid AgentId { get; set; }
    public Agent Agent { get; set; } = null!;
    public required string KeyHash { get; set; }
    public required string KeyPrefix { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }

    public bool IsUsable(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
