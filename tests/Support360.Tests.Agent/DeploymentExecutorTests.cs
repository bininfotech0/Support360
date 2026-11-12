using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Support360.Agent;

namespace Support360.Tests.Agent;

public sealed class DeploymentExecutorTests
{
    [Fact]
    public void VerifyManifestAcceptsSignedTargetBoundWork()
    {
        var (key, manifest, deploy) = SignedManifest();
        var identity = new AgentIdentityValue(manifest.AgentId, manifest.TenantId, "api-key", false);
        DeploymentExecutor.VerifyManifest(manifest, key, identity, deploy, manifest.IssuedAt.AddMinutes(1));
    }

    [Fact]
    public void VerifyManifestRejectsBindingAndReplayViolations()
    {
        var (key, manifest, deploy) = SignedManifest();
        var now = manifest.IssuedAt.AddMinutes(1);

        Assert.Throws<InvalidOperationException>(() => DeploymentExecutor.VerifyManifest(
            manifest, key, new AgentIdentityValue("other-agent", manifest.TenantId, "api-key", false), deploy, now));
        Assert.Throws<InvalidOperationException>(() => DeploymentExecutor.VerifyManifest(
            manifest, key, new AgentIdentityValue(manifest.AgentId, "other-tenant", "api-key", false), deploy, now));
        Assert.Throws<InvalidOperationException>(() => DeploymentExecutor.VerifyManifest(
            manifest, key, new AgentIdentityValue(manifest.AgentId, manifest.TenantId, "api-key", false),
            deploy, manifest.ExpiresAt.AddSeconds(1)));

        var tampered = manifest with { TargetPath = "/tmp/other" };
        Assert.Throws<InvalidOperationException>(() => DeploymentExecutor.VerifyManifest(
            tampered, key, new AgentIdentityValue(manifest.AgentId, manifest.TenantId, "api-key", false), deploy, now));

        var wrongDigest = deploy with { PackageSha256 = "different" };
        Assert.Throws<InvalidOperationException>(() => DeploymentExecutor.VerifyManifest(
            manifest, key, new AgentIdentityValue(manifest.AgentId, manifest.TenantId, "api-key", false), wrongDigest, now));
    }

    [Fact]
    public void ResolveDeploymentTargetRejectsEscape()
    {
        var root = NewTempDirectory();
        try
        {
            DeploymentExecutor.ResolveDeploymentTarget(root, Path.Combine(root, "app"));

            Assert.Throws<InvalidOperationException>(() =>
                DeploymentExecutor.ResolveDeploymentTarget(root, Path.GetDirectoryName(root)!));
            Assert.Throws<InvalidOperationException>(() =>
                DeploymentExecutor.ResolveDeploymentTarget(root, root));
            Assert.Throws<InvalidOperationException>(() =>
                DeploymentExecutor.ResolveDeploymentTarget(root, Path.Combine(root, "..", "escape")));
            Assert.Throws<InvalidOperationException>(() =>
                DeploymentExecutor.ResolveDeploymentTarget(root, "  "));
            Assert.Throws<InvalidOperationException>(() =>
                DeploymentExecutor.ResolveDeploymentTarget(root, Path.Combine(root, "app\nevade")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ExtractArtifactRejectsZipSlipAndSymlinks()
    {
        var slip = ZipBytes(new Dictionary<string, string> { ["../escape.txt"] = "bad" });
        Assert.Throws<InvalidOperationException>(() =>
            DeploymentExecutor.ExtractArtifact(slip, NewTempDirectory()));

        using var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = writer.CreateEntry("link");
            entry.ExternalAttributes = 0xA1FF << 16;
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes("target"));
        }
        Assert.Throws<InvalidOperationException>(() =>
            DeploymentExecutor.ExtractArtifact(buffer.ToArray(), NewTempDirectory()));
    }

    [Fact]
    public void ExtractArtifactRejectsAbsoluteDriveLetterAndDuplicatePaths()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DeploymentExecutor.ExtractArtifact(ZipBytes(new Dictionary<string, string> { ["/absolute.txt"] = "bad" }), NewTempDirectory()));
        Assert.Throws<InvalidOperationException>(() =>
            DeploymentExecutor.ExtractArtifact(ZipBytes(new Dictionary<string, string> { ["C:/escape.txt"] = "bad" }), NewTempDirectory()));
        Assert.Throws<InvalidOperationException>(() =>
            DeploymentExecutor.ExtractArtifact(ZipBytes(new Dictionary<string, string> { ["./loop/../loop.txt"] = "a", ["loop.txt"] = "b" }), NewTempDirectory()));
    }

    [Fact]
    public void ExtractArtifactWritesRegularFiles()
    {
        var archive = ZipBytes(new Dictionary<string, string> { ["index.html"] = "hello", ["assets/app.js"] = "ok" });
        var destination = NewTempDirectory();
        DeploymentExecutor.ExtractArtifact(archive, destination);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(destination, "index.html")));
        Assert.True(File.Exists(Path.Combine(destination, "assets", "app.js")), "nested file missing");
    }

    [Fact]
    public void LoadManifestPublicKeyAcceptsInlinePemAndPemFile()
    {
        using var key = RSA.Create(2048);
        var pem = key.ExportSubjectPublicKeyInfoPem();

        Assert.Null(DeploymentExecutor.LoadManifestPublicKey(null));
        Assert.Null(DeploymentExecutor.LoadManifestPublicKey("  "));
        Assert.Null(DeploymentExecutor.LoadManifestPublicKey(@"C:\nonexistent-" + Guid.NewGuid() + ".pem"));

        var path = Path.Combine(NewTempDirectory(), "key.pem");
        File.WriteAllText(path, pem);
        using (var fromFile = DeploymentExecutor.LoadManifestPublicKey(path))
        {
            Assert.NotNull(fromFile);
        }
        using (var inline = DeploymentExecutor.LoadManifestPublicKey(pem))
        {
            Assert.NotNull(inline);
        }
    }

    [Fact]
    public async Task EndToEndDeploymentVerifiesSignaturesExtractsAndSwaps()
    {
        using var key = RSA.Create(2048);
        var artifact = ZipBytes(new Dictionary<string, string> { ["index.html"] = "v2" });
        var digest = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant();

        var root = NewTempDirectory();
        var targetPath = Path.Combine(root, "app");
        var manifestPath = Path.Combine(root, "manifest-public-key.pem");
        File.WriteAllText(manifestPath, key.ExportSubjectPublicKeyInfoPem());

        var issued = TruncateToSeconds(DateTimeOffset.UtcNow.AddMinutes(-1));
        var manifest = Resign(new DeploymentManifest(
            "deployment-1",
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333",
            "web",
            "1.2.3",
            digest,
            null,
            null,
            targetPath,
            "nonce-1",
            issued,
            issued.AddMinutes(30),
            "RSASSA-PSS-SHA256",
            "test-key",
            ""), key);
        var deploy = new DeploymentRequest("deployment-1", "web", "1.2.3", digest, null, null, targetPath, "Production");

        Directory.CreateDirectory(targetPath);
        File.WriteAllText(Path.Combine(targetPath, "index.html"), "v1");

        var serveTamperedManifest = false;
        using var server = new TestHttpServer(context =>
        {
            switch (context.Request.Url!.AbsolutePath)
            {
                case "/api/agent/v1/deployments/deployment-1/manifest":
                    TestHttpServer.RespondJson(context, serveTamperedManifest
                        ? manifest with { TargetPath = Path.Combine(root, "attacker-target") }
                        : manifest);
                    break;
                case "/api/agent/v1/deployments/deployment-1/artifact":
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/zip";
                    context.Response.ContentLength64 = artifact.Length;
                    context.Response.OutputStream.Write(artifact);
                    break;
                default:
                    TestHttpServer.Respond(context, 404);
                    break;
            }
        });

        var identity = new AgentIdentityValue(
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333",
            "api-key",
            DeploymentOptIn: true);
        using var client = AgentClient.Create(server.Url, allowHttp: true);

        var result = await DeploymentExecutor.RunDeploymentAsync(
            client, "api-key", identity, deploy,
            new DeploymentRuntimeConfig(manifestPath, root), CancellationToken.None);

        Assert.Equal("succeeded", result.Status);
        Assert.Null(result.Detail);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(targetPath, "index.html")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.GetDirectoryName(targetPath)!, ".support360-*"));

        // A manifest whose target was modified after signing must be rejected
        // without touching the running deployment.
        serveTamperedManifest = true;
        var rejected = await DeploymentExecutor.RunDeploymentAsync(
            client, "api-key", identity, deploy,
            new DeploymentRuntimeConfig(manifestPath, root), CancellationToken.None);

        Assert.Equal("failed", rejected.Status);
        Assert.Contains("manifest", rejected.Detail);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(targetPath, "index.html")));
        Assert.False(Directory.Exists(Path.Combine(root, "attacker-target")));
    }

    private static (RSA Key, DeploymentManifest Manifest, DeploymentRequest Deploy) SignedManifest()
    {
        var key = RSA.Create(2048);
        var issued = TruncateToSeconds(DateTimeOffset.UtcNow.AddMinutes(-1));
        var manifest = Resign(new DeploymentManifest(
            "deployment-1",
            "22222222-2222-2222-2222-222222222222",
            "33333333-3333-3333-3333-333333333333",
            "web",
            "1.2.3",
            "aabbcc",
            null,
            null,
            "/srv/support360/app",
            "nonce-1",
            issued,
            issued.AddMinutes(30),
            "RSASSA-PSS-SHA256",
            "test-key",
            ""), key);
        var deploy = new DeploymentRequest(manifest.DeploymentId, manifest.PackageName, manifest.PackageVersion,
            manifest.PackageSha256, null, null, manifest.TargetPath, "Production");
        return (key, manifest, deploy);
    }

    private static DeploymentManifest Resign(DeploymentManifest manifest, RSA key)
    {
        var payload = Encoding.UTF8.GetBytes(DeploymentExecutor.CanonicalManifestPayload(manifest));
        var signature = key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return manifest with { Signature = Convert.ToBase64String(signature) };
    }

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));

    private static byte[] ZipBytes(IReadOnlyDictionary<string, string> files)
    {
        using var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = writer.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        return buffer.ToArray();
    }

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "support360-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
