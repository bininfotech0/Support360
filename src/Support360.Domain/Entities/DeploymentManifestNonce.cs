using Support360.Domain;

namespace Support360.Domain.Entities;

public sealed class DeploymentManifestNonce : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Nonce { get; set; }
    public Guid DeploymentId { get; set; }
    public bool Used { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
