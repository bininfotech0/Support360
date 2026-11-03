using System.Security.Cryptography;
using System.IO.Compression;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Contracts.Deployments;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class DeploymentPackageService : IDeploymentPackageService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IValidator<UploadDeploymentPackageRequest> _validator;

    public DeploymentPackageService(ISupport360DbContext db, ITenantContext tenantContext, IClock clock,
        IAuditService audit, IValidator<UploadDeploymentPackageRequest> validator)
    {
        _db = db; _tenantContext = tenantContext; _clock = clock; _audit = audit; _validator = validator;
    }

    public async Task<IReadOnlyList<DeploymentPackageDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        return await _db.DeploymentPackages.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.UploadedAt).Take(200)
            .Select(package => new DeploymentPackageDto(
                package.Id, package.Name, package.Version, package.Sha256, package.Content.Length, package.UploadedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<DeploymentPackageDto?> GetAsync(Guid tenantId, Guid packageId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        return await _db.DeploymentPackages.AsNoTracking()
            .Where(package => package.TenantId == tenantId && package.Id == packageId)
            .Select(package => new DeploymentPackageDto(
                package.Id, package.Name, package.Version, package.Sha256, package.Content.Length, package.UploadedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<DeploymentPackageDto> UploadAsync(RequestActor actor, UploadDeploymentPackageRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(actor.TenantId);
        ValidateZip(request.Content);

        var name = request.Name.Trim();
        var version = request.Version.Trim();
        if (await _db.DeploymentPackages.AnyAsync(x => x.TenantId == actor.TenantId && x.Name == name && x.Version == version, cancellationToken))
            throw new ConflictException("A package with this name and version already exists in this tenant.");

        var package = new DeploymentPackage
        {
            TenantId = actor.TenantId,
            Name = name,
            Version = version,
            Sha256 = Convert.ToHexString(SHA256.HashData(request.Content)).ToLowerInvariant(),
            Content = request.Content,
            UploadedByUserId = actor.UserId ?? Guid.Empty,
            UploadedAt = _clock.UtcNow,
            CreatedAt = _clock.UtcNow
        };
        _db.DeploymentPackages.Add(package);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "deployment-package.uploaded", "DeploymentPackage", package.Id.ToString(), "success",
            new { package.Name, package.Version, package.Sha256, SizeBytes = package.Content.LongLength }, actor.IpAddress, cancellationToken);
        return ToDto(package);
    }

    private static DeploymentPackageDto ToDto(DeploymentPackage package) =>
        new(package.Id, package.Name, package.Version, package.Sha256, package.Content.LongLength, package.UploadedAt);

    private static void ValidateZip(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count is 0 or > DeploymentPackageLimits.MaximumEntries)
                throw new FluentValidation.ValidationException("ZIP package entry count is invalid or exceeds the limit.");

            long expandedBytes = 0;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName.Replace('\\', '/');
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)
                    || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
                    || segments.Any(segment => segment is ".." or ".")
                    || path.Contains('\0') || !paths.Add(path))
                    throw new FluentValidation.ValidationException("ZIP package contains an unsafe or duplicate path.");

                var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                var dosAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
                if (unixType == 0xA000 || dosAttributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new FluentValidation.ValidationException("ZIP package may not contain symbolic links or reparse points.");

                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > DeploymentPackageLimits.MaximumExpandedBytes)
                    throw new FluentValidation.ValidationException("ZIP package expands beyond the 100 MiB limit.");
            }
        }
        catch (InvalidDataException)
        {
            throw new FluentValidation.ValidationException("The uploaded file is not a valid ZIP archive.");
        }
        catch (OverflowException)
        {
            throw new FluentValidation.ValidationException("ZIP package has invalid expanded size metadata.");
        }
    }
}
