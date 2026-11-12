using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Support360.Application.Options;
using Support360.Infrastructure.Services;

namespace Support360.Tests.Unit;

public sealed class DeploymentManifestServiceTests
{
    [Fact]
    public void SigningFailsClosedWhenExecutionIsDisabled()
    {
        var service = CreateService(new DeploymentOptions());
        Assert.False(service.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => service.SignManifest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "app", "1.0", new string('a', 64),
            null, null, "/srv/app", "nonce", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public void SigningFailsClosedWithoutAKey()
    {
        var service = CreateService(new DeploymentOptions { ExecutionEnabled = true });
        Assert.False(service.IsConfigured);
    }

    [Fact]
    public void SignedManifestVerifiesAndRejectsTampering()
    {
        using var rsa = RSA.Create(2048);
        var service = CreateService(new DeploymentOptions
        {
            ExecutionEnabled = true,
            ManifestSigningPrivateKey = rsa.ExportPkcs8PrivateKeyPem(),
            ManifestSigningPublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            ManifestSigningKeyId = "unit-test-key"
        });
        Assert.True(service.IsConfigured);

        var issued = DateTimeOffset.UtcNow;
        var manifest = service.SignManifest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "web", "2.0.0", new string('b', 64), "https://example.test/repo.git", "main",
            "/srv/support360/web", "nonce-unit-test", issued, issued.AddMinutes(15));

        Assert.Equal(DeploymentManifestService.SignatureAlgorithm, manifest.SignatureAlgorithm);
        Assert.Equal("unit-test-key", manifest.KeyId);
        Assert.True(service.VerifyManifest(manifest));
        Assert.False(service.VerifyManifest(manifest with { TargetPath = "/srv/support360/other" }));
        Assert.False(service.VerifyManifest(manifest with { PackageSha256 = new string('c', 64) }));
        Assert.False(service.VerifyManifest(manifest with { KeyId = "unknown-key" }));
    }

    [Fact]
    public void CanonicalPayloadIsStableAndUsesEncodedFields()
    {
        var deploymentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var agentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var tenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var issued = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var payload = DeploymentManifestService.BuildCanonicalPayload(deploymentId, agentId, tenantId,
            "app|with-newline\nname", "1.0", new string('A', 64), "/srv/app", "nonce", issued, issued.AddMinutes(5));

        Assert.StartsWith("S360M1\n", payload);
        Assert.Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("app|with-newline\nname")), payload);
        Assert.Contains(new string('a', 64), payload);
        Assert.DoesNotContain("app|with-newline", payload);
    }

    private static DeploymentManifestService CreateService(DeploymentOptions options) =>
        new(Options.Create(options));
}
