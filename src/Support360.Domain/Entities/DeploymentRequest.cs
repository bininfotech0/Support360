namespace Support360.Domain.Entities;

public sealed class DeploymentRequest : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid TargetAgentId { get; set; }
    public Agent TargetAgent { get; set; } = null!;
    public Guid DeploymentPackageId { get; set; }
    public DeploymentPackage Package { get; set; } = null!;
    public required string PackageName { get; set; }
    public required string PackageVersion { get; set; }
    public required string PackageSha256 { get; set; }
    public Guid ChangeRequestId { get; set; }
    public ChangeRequest ChangeRequest { get; set; } = null!;
    public required string Environment { get; set; }
    public string? GitRepoURL { get; set; }
    public string? GitBranch { get; set; }
    public string? TargetPath { get; set; }
    public DeploymentRequestStatus Status { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public required string RequestedBySubject { get; set; }
    public required string RequestedByDisplay { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
