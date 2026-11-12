namespace Support360.Contracts.Deployments;

public sealed record CreateDeploymentRequest(
    Guid TargetAgentId,
    Guid DeploymentPackageId,
    Guid ChangeTicketId,
    string? GitRepoURL,
    string? GitBranch,
    string? TargetPath);

public sealed record DeploymentRequestDto(
    Guid Id,
    Guid TargetAgentId,
    string TargetHostname,
    Guid DeploymentPackageId,
    string PackageName,
    string PackageVersion,
    string PackageSha256,
    long PackageSizeBytes,
    Guid ChangeTicketId,
    string ChangeTicketNumber,
    string Environment,
    string? GitRepoURL,
    string? GitBranch,
    string? TargetPath,
    string Status,
    int RequiredApprovals,
    int ReceivedApprovals,
    Guid? RequestedByUserId,
    string RequestedByDisplay,
    DateTimeOffset RequestedAt);

public sealed record DeploymentManifestDto(
    Guid DeploymentId,
    Guid AgentId,
    Guid TenantId,
    string PackageName,
    string PackageVersion,
    string PackageSha256,
    string? GitRepoURL,
    string? GitBranch,
    string? TargetPath,
    string Nonce,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string SignatureAlgorithm,
    string KeyId,
    string Signature);

public sealed record DeploymentArtifactDto(byte[] Content, string FileName, string Sha256);
