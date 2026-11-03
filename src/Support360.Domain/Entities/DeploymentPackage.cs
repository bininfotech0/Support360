namespace Support360.Domain.Entities;

public sealed class DeploymentPackage : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string Sha256 { get; set; }
    public required byte[] Content { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}
