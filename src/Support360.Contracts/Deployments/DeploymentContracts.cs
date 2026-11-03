namespace Support360.Contracts.Deployments;

public sealed record CreateDeploymentRequest(
    Guid TargetAgentId,
    Guid DeploymentPackageId,
    Guid ChangeRequestId);

public sealed record DeploymentRequestDto(
    Guid Id,
    Guid TargetAgentId,
    string TargetHostname,
    Guid DeploymentPackageId,
    string PackageName,
    string PackageVersion,
    string PackageSha256,
    long PackageSizeBytes,
    Guid ChangeRequestId,
    string ChangeRequestNumber,
    string Environment,
    string Status,
    int RequiredApprovals,
    int ReceivedApprovals,
    Guid? RequestedByUserId,
    string RequestedByDisplay,
    DateTimeOffset RequestedAt);
