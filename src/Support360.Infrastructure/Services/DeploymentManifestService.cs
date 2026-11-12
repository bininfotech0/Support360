using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Application.Options;
using Support360.Contracts.Deployments;

namespace Support360.Infrastructure.Services;

public sealed class DeploymentManifestService : IDeploymentManifestService
{
    public const string SignatureAlgorithm = "RSASSA-PSS-SHA256";

    private readonly DeploymentOptions _options;

    public DeploymentManifestService(IOptions<DeploymentOptions> options) => _options = options.Value;

    public bool IsConfigured
    {
        get
        {
            if (!_options.ExecutionEnabled || string.IsNullOrWhiteSpace(_options.ManifestSigningPrivateKey))
            {
                return false;
            }

            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(_options.ManifestSigningPrivateKey);
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }

    public DeploymentManifestDto SignManifest(Guid deploymentId, Guid agentId, Guid tenantId,
        string packageName, string packageVersion, string packageSha256,
        string? gitRepoURL, string? gitBranch, string targetPath,
        string nonce, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        if (!_options.ExecutionEnabled)
        {
            throw new InvalidOperationException("Deployment execution is disabled by server policy.");
        }

        if (string.IsNullOrWhiteSpace(_options.ManifestSigningPrivateKey))
        {
            throw new InvalidOperationException("Deployment manifest signing is not configured.");
        }

        var payload = BuildCanonicalPayload(deploymentId, agentId, tenantId, packageName, packageVersion,
            packageSha256, targetPath, nonce, issuedAt, expiresAt);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_options.ManifestSigningPrivateKey);
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);

        return new DeploymentManifestDto(deploymentId, agentId, tenantId, packageName, packageVersion,
            packageSha256, gitRepoURL, gitBranch, targetPath, nonce, issuedAt, expiresAt,
            SignatureAlgorithm, _options.ManifestSigningKeyId, Convert.ToBase64String(signature));
    }

    public bool VerifyManifest(DeploymentManifestDto manifest)
    {
        if (_options.ExecutionEnabled == false || string.IsNullOrWhiteSpace(_options.ManifestSigningPublicKey))
        {
            return false;
        }

        try
        {
            if (!string.Equals(manifest.SignatureAlgorithm, SignatureAlgorithm, StringComparison.Ordinal)
                || !string.Equals(manifest.KeyId, _options.ManifestSigningKeyId, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = BuildCanonicalPayload(manifest.DeploymentId, manifest.AgentId, manifest.TenantId,
                manifest.PackageName, manifest.PackageVersion, manifest.PackageSha256,
                manifest.TargetPath ?? string.Empty, manifest.Nonce, manifest.IssuedAt, manifest.ExpiresAt);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(_options.ManifestSigningPublicKey);
            return rsa.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromBase64String(manifest.Signature),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return false;
        }
    }

    public static string BuildCanonicalPayload(Guid deploymentId, Guid agentId, Guid tenantId,
        string packageName, string packageVersion, string packageSha256, string targetPath,
        string nonce, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        return string.Join('\n',
            "S360M1",
            deploymentId.ToString("D", CultureInfo.InvariantCulture),
            agentId.ToString("D", CultureInfo.InvariantCulture),
            tenantId.ToString("D", CultureInfo.InvariantCulture),
            Encode(packageName),
            Encode(packageVersion),
            packageSha256.ToLowerInvariant(),
            Encode(targetPath),
            Encode(nonce),
            issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }
}
