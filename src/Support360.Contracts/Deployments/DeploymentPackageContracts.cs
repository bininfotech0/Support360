namespace Support360.Contracts.Deployments;

public sealed record DeploymentPackageDto(
    Guid Id,
    string Name,
    string Version,
    string Sha256,
    long SizeBytes,
    DateTimeOffset UploadedAt);

public sealed record UploadDeploymentPackageRequest(string Name, string Version, byte[] Content);

public static class DeploymentPackageLimits
{
    public const int MaximumPackageBytes = 20 * 1024 * 1024;
    public const long MaximumExpandedBytes = 100 * 1024 * 1024;
    public const int MaximumEntries = 2_000;
}
