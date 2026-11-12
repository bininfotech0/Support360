using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Support360.Agent;

public sealed record DeploymentRuntimeConfig(string? ManifestPublicKeyPath, string? DeploymentRoot);

public static class DeploymentExecutor
{
    public const string ManifestSignatureAlgorithm = "RSASSA-PSS-SHA256";
    private const int MaximumArtifactEntries = 2000;
    private const long MaximumExpandedBytes = 100 * 1024 * 1024;
    private static readonly TimeSpan MaximumManifestLifetime = TimeSpan.FromHours(2);
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
    private const UnixFileMode EntryFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead;

    public static async Task<DeploymentResult> RunDeploymentAsync(
        AgentClient client, string apiKey, AgentIdentityValue identity, DeploymentRequest deploy,
        DeploymentRuntimeConfig config, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;

        DeploymentResult Fail(string detail)
        {
            AgentLog.Error("deployment failed", ("deploymentId", deploy.Id), ("detail", detail),
                ("durationMs", (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds));
            return new DeploymentResult(deploy.Id, "failed", detail);
        }

        if (!identity.DeploymentOptIn)
        {
            return Fail("deployment execution is not enabled for this local agent");
        }
        if (string.IsNullOrWhiteSpace(config.DeploymentRoot))
        {
            return Fail("no deployment root is configured locally");
        }

        DeploymentManifest manifest;
        try
        {
            manifest = await client.GetDeploymentManifestAsync(apiKey, deploy.Id, cancellationToken)
                ?? throw new InvalidOperationException("server returned an empty deployment manifest");
        }
        catch (Exception ex)
        {
            return Fail("retrieve deployment manifest: " + ex.Message);
        }

        using var publicKey = LoadManifestPublicKey(config.ManifestPublicKeyPath);
        if (publicKey is null)
        {
            return Fail("load manifest verification key: SUPPORT360_MANIFEST_PUBLIC_KEY or --manifest-public-key is required");
        }

        try
        {
            VerifyManifest(manifest, publicKey, identity, deploy, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            return Fail("reject deployment manifest: " + ex.Message);
        }

        string targetPath;
        try
        {
            targetPath = ResolveDeploymentTarget(config.DeploymentRoot, manifest.TargetPath);
        }
        catch (Exception ex)
        {
            return Fail("reject deployment target: " + ex.Message);
        }

        byte[] artifact;
        try
        {
            artifact = await client.DownloadDeploymentArtifactAsync(apiKey, deploy.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            return Fail("download deployment artifact: " + ex.Message);
        }

        var actualDigest = Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant();
        if (!string.Equals(actualDigest, manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Fail($"deployment artifact digest mismatch: expected {manifest.PackageSha256}, received {actualDigest}");
        }

        var stagingPath = Path.Combine(Path.GetDirectoryName(targetPath) ?? ".",
            ".support360-staging-" + SafeName(deploy.Id) + "-" + SafeName(manifest.Nonce));
        var backupPath = Path.Combine(Path.GetDirectoryName(targetPath) ?? ".",
            ".support360-backup-" + SafeName(deploy.Id) + "-" + SafeName(manifest.Nonce));
        TryDelete(stagingPath);
        TryDelete(backupPath);

        try
        {
            ExtractArtifact(artifact, stagingPath);
        }
        catch (Exception ex)
        {
            TryDelete(stagingPath);
            return Fail("extract deployment artifact: " + ex.Message);
        }

        var hadPrevious = false;
        try
        {
            if (Directory.Exists(targetPath))
            {
                Directory.Move(targetPath, backupPath);
                hadPrevious = true;
            }
            else if (File.Exists(targetPath))
            {
                File.Move(targetPath, backupPath);
                hadPrevious = true;
            }

            Directory.Move(stagingPath, targetPath);
        }
        catch (Exception ex)
        {
            if (hadPrevious)
            {
                RestoreBackup(backupPath, targetPath);
            }
            TryDelete(stagingPath);
            return Fail("activate deployment: " + ex.Message);
        }

        if (hadPrevious)
        {
            TryDelete(backupPath);
        }

        AgentLog.Info("deployment succeeded", ("deploymentId", deploy.Id),
            ("durationMs", (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds));
        return new DeploymentResult(deploy.Id, "succeeded", null);
    }

    public static RSA? LoadManifestPublicKey(string? pathOrPem)
    {
        if (string.IsNullOrWhiteSpace(pathOrPem))
        {
            return null;
        }
        try
        {
            var data = pathOrPem.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal)
                || pathOrPem.Contains("BEGIN RSA PUBLIC KEY", StringComparison.Ordinal)
                ? pathOrPem
                : File.ReadAllText(pathOrPem);
            var rsa = RSA.Create();
            rsa.ImportFromPem(data);
            return rsa;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void VerifyManifest(DeploymentManifest manifest, RSA publicKey, AgentIdentityValue identity,
        DeploymentRequest deploy, DateTimeOffset now)
    {
        if (manifest.SignatureAlgorithm != ManifestSignatureAlgorithm)
        {
            throw new InvalidOperationException($"unsupported signature algorithm \"{manifest.SignatureAlgorithm}\"");
        }
        if (manifest.DeploymentId != deploy.Id)
        {
            throw new InvalidOperationException("manifest deployment id does not match the dispatched work");
        }
        if (manifest.AgentId != identity.AgentId)
        {
            throw new InvalidOperationException("manifest is bound to a different agent");
        }
        if (manifest.TenantId != identity.TenantId)
        {
            throw new InvalidOperationException("manifest is bound to a different tenant");
        }
        if (!string.Equals(manifest.PackageSha256, deploy.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("manifest package digest does not match the dispatched work");
        }
        if (manifest.PackageName != deploy.PackageName || manifest.PackageVersion != deploy.PackageVersion)
        {
            throw new InvalidOperationException("manifest package identity does not match the dispatched work");
        }
        if (string.IsNullOrEmpty(manifest.Nonce) || manifest.Nonce.Length > 128)
        {
            throw new InvalidOperationException("manifest nonce is invalid");
        }
        if (manifest.IssuedAt > now.Add(MaximumClockSkew))
        {
            throw new InvalidOperationException("manifest issue time is in the future");
        }
        if (now >= manifest.ExpiresAt)
        {
            throw new InvalidOperationException("manifest has expired");
        }
        if (manifest.ExpiresAt - manifest.IssuedAt > MaximumManifestLifetime)
        {
            throw new InvalidOperationException("manifest lifetime exceeds the local maximum");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(manifest.Signature);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("manifest signature is not valid base64", ex);
        }

        var payload = CanonicalManifestPayload(manifest);
        if (!publicKey.VerifyData(Encoding.UTF8.GetBytes(payload), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
        {
            throw new InvalidOperationException("manifest signature verification failed");
        }
    }

    public static string CanonicalManifestPayload(DeploymentManifest manifest)
    {
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        return string.Join('\n',
            "S360M1",
            manifest.DeploymentId,
            manifest.AgentId,
            manifest.TenantId,
            Encode(manifest.PackageName),
            Encode(manifest.PackageVersion),
            manifest.PackageSha256.ToLowerInvariant(),
            Encode(manifest.TargetPath ?? string.Empty),
            Encode(manifest.Nonce),
            manifest.IssuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            manifest.ExpiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    public static string ResolveDeploymentTarget(string rootValue, string? targetValue)
    {
        if (string.IsNullOrWhiteSpace(rootValue))
        {
            throw new InvalidOperationException("deployment root is not configured");
        }
        if (string.IsNullOrWhiteSpace(targetValue))
        {
            throw new InvalidOperationException("target path is empty");
        }
        if (targetValue.Contains('\0') || targetValue.Contains('\r') || targetValue.Contains('\n'))
        {
            throw new InvalidOperationException("target path contains unsafe characters");
        }

        var root = Path.GetFullPath(rootValue);
        AtomicIO.CreateDirectory(root, DirectoryMode);

        var target = Path.GetFullPath(targetValue);
        var relative = Path.GetRelativePath(root, target);
        if (relative == "." || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException("target path must be a subdirectory of the configured deployment root");
        }

        AtomicIO.CreateDirectory(Path.GetDirectoryName(target) ?? ".", DirectoryMode);
        return target;
    }

    public static void ExtractArtifact(byte[] artifact, string destination)
    {
        using var archive = new ZipArchive(new MemoryStream(artifact), ZipArchiveMode.Read);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumArtifactEntries)
        {
            throw new InvalidOperationException("archive entry count is invalid or exceeds the limit");
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeEntryPath(entry);
            if (!paths.Add(normalized))
            {
                throw new InvalidOperationException($"archive contains a duplicate path \"{entry.FullName}\"");
            }
            expanded += entry.Length;
            if (expanded > MaximumExpandedBytes)
            {
                throw new InvalidOperationException("archive expands beyond the local limit");
            }
        }

        AtomicIO.CreateDirectory(destination, DirectoryMode);
        long written = 0;
        foreach (var entry in archive.Entries)
        {
            var relative = NormalizeEntryPath(entry);
            var target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
            var containment = Path.GetRelativePath(destination, target);
            if (containment == ".." || containment.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"archive entry escapes the staging directory: \"{entry.FullName}\"");
            }

            if (IsDirectoryEntry(entry))
            {
                AtomicIO.CreateDirectory(target, DirectoryMode);
                continue;
            }

            AtomicIO.CreateDirectory(Path.GetDirectoryName(target) ?? ".", DirectoryMode);
            using (var source = entry.Open())
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = source.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }
                    output.Write(buffer, 0, read);
                    written += read;
                    if (written > MaximumExpandedBytes)
                    {
                        throw new InvalidOperationException("archive expands beyond the local limit");
                    }
                }
            }
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(target, EntryFileMode);
            }
        }
    }

    internal static string NormalizeEntryPath(ZipArchiveEntry entry)
    {
        var name = entry.FullName.Replace('\\', '/');
        if (name.Length == 0
            || name[0] == '/'
            || name.Contains('\0')
            || (name.Length >= 2 && name[1] == ':'))
        {
            throw new InvalidOperationException($"archive contains an unsafe path \"{entry.FullName}\"");
        }

        var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                throw new InvalidOperationException($"archive contains an unsafe path \"{entry.FullName}\"");
            }
            if (segment == ".")
            {
                continue;
            }
            normalized.Add(segment);
        }
        if (normalized.Count == 0)
        {
            throw new InvalidOperationException($"archive contains an unsafe path \"{entry.FullName}\"");
        }

        var unixMode = entry.ExternalAttributes >> 16;
        if (unixMode != 0)
        {
            var fileType = unixMode & 0xF000;
            if (fileType is not (0x8000 or 0x4000))
            {
                throw new InvalidOperationException($"archive contains an unsupported entry type \"{entry.FullName}\"");
            }
        }

        return string.Join('/', normalized);
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')
        || ((entry.ExternalAttributes >> 16) & 0xF000) == 0x4000;

    public static string SafeName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if ((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9') || character == '-')
            {
                builder.Append(character);
            }
        }
        if (builder.Length == 0)
        {
            return "deployment";
        }
        return builder.Length > 64 ? builder.ToString(0, 64) : builder.ToString();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void RestoreBackup(string backupPath, string targetPath)
    {
        try
        {
            if (Directory.Exists(backupPath))
            {
                Directory.Move(backupPath, targetPath);
            }
            else if (File.Exists(backupPath))
            {
                File.Move(backupPath, targetPath);
            }
        }
        catch (IOException)
        {
            AgentLog.Error("deployment rollback failed", ("backup", backupPath), ("target", targetPath));
        }
    }
}
