using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Application.Options;
using Support360.Contracts.Deployments;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class DeploymentService : IDeploymentService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IRealtimeNotifier _realtime;
    private readonly IValidator<CreateDeploymentRequest> _validator;
    private readonly IDeploymentManifestService _manifest;
    private readonly DeploymentOptions _options;

    public DeploymentService(ISupport360DbContext db, ITenantContext tenantContext, IClock clock,
        IAuditService audit, IRealtimeNotifier realtime, IValidator<CreateDeploymentRequest> validator,
        IDeploymentManifestService manifest, IOptions<DeploymentOptions> options)
    {
        _db = db; _tenantContext = tenantContext; _clock = clock; _audit = audit; _realtime = realtime;
        _validator = validator; _manifest = manifest; _options = options.Value;
    }

    public async Task<IReadOnlyList<DeploymentRequestDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var rows = await _db.DeploymentRequests.AsNoTracking()
            .Where(deployment => deployment.TenantId == tenantId)
            .OrderByDescending(deployment => deployment.RequestedAt)
            .Take(200)
            .Select(deployment => new
            {
                deployment.Id, deployment.TargetAgentId, TargetHostname = deployment.TargetAgent.Hostname,
                deployment.DeploymentPackageId, deployment.PackageName, deployment.PackageVersion, deployment.PackageSha256,
                PackageSizeBytes = deployment.Package.Content.Length, deployment.ChangeTicketId,
                ChangeTicketNumber = deployment.ChangeTicket.Number, deployment.Environment,
                deployment.GitRepoURL, deployment.GitBranch, deployment.TargetPath,
                deployment.Status, ChangeStatus = deployment.ChangeTicket.Status,
                deployment.ChangeTicket.RequiredApprovals,
                ReceivedApprovals = deployment.ChangeTicket.Approvals.Count, deployment.RequestedByUserId,
                deployment.RequestedByDisplay, deployment.RequestedAt
            })
            .ToArrayAsync(cancellationToken);
        return rows.Select(deployment => new DeploymentRequestDto(
            deployment.Id, deployment.TargetAgentId, deployment.TargetHostname, deployment.DeploymentPackageId,
            deployment.PackageName, deployment.PackageVersion, deployment.PackageSha256, deployment.PackageSizeBytes,
            deployment.ChangeTicketId, deployment.ChangeTicketNumber, deployment.Environment,
            deployment.GitRepoURL, deployment.GitBranch, deployment.TargetPath,
            deployment.Status >= DeploymentRequestStatus.Queued ? deployment.Status.ToString() : deployment.ChangeStatus.ToString(),
            deployment.RequiredApprovals, deployment.ReceivedApprovals, deployment.RequestedByUserId,
            deployment.RequestedByDisplay, deployment.RequestedAt)).ToArray();
    }

    public async Task<DeploymentRequestDto?> GetAsync(Guid tenantId, Guid deploymentId, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var row = await _db.DeploymentRequests.AsNoTracking()
            .Where(deployment => deployment.TenantId == tenantId && deployment.Id == deploymentId)
            .Select(deployment => new
            {
                deployment.Id, deployment.TargetAgentId, TargetHostname = deployment.TargetAgent.Hostname,
                deployment.DeploymentPackageId, deployment.PackageName, deployment.PackageVersion, deployment.PackageSha256,
                PackageSizeBytes = deployment.Package.Content.Length, deployment.ChangeTicketId,
                ChangeTicketNumber = deployment.ChangeTicket.Number, deployment.Environment,
                deployment.GitRepoURL, deployment.GitBranch, deployment.TargetPath,
                deployment.Status, ChangeStatus = deployment.ChangeTicket.Status,
                deployment.ChangeTicket.RequiredApprovals,
                ReceivedApprovals = deployment.ChangeTicket.Approvals.Count, deployment.RequestedByUserId,
                deployment.RequestedByDisplay, deployment.RequestedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new DeploymentRequestDto(
            row.Id, row.TargetAgentId, row.TargetHostname, row.DeploymentPackageId, row.PackageName, row.PackageVersion,
            row.PackageSha256, row.PackageSizeBytes, row.ChangeTicketId, row.ChangeTicketNumber, row.Environment,
            row.GitRepoURL, row.GitBranch, row.TargetPath,
            row.Status >= DeploymentRequestStatus.Queued ? row.Status.ToString() : row.ChangeStatus.ToString(),
            row.RequiredApprovals, row.ReceivedApprovals, row.RequestedByUserId, row.RequestedByDisplay, row.RequestedAt);
    }

    public async Task<DeploymentRequestDto> CreateAsync(RequestActor actor, CreateDeploymentRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(actor.Subject))
            throw new ForbiddenException("A stable requester identity is required for deployment requests.");

        _tenantContext.SetTenant(actor.TenantId);
        var agent = await _db.Agents.AsNoTracking()
            .Where(x => x.TenantId == actor.TenantId && x.Id == request.TargetAgentId && x.Status == AgentStatus.Active)
            .Select(x => new { x.Id, x.Hostname })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Active target agent not found.");

        var package = await _db.DeploymentPackages.AsNoTracking()
            .Where(x => x.TenantId == actor.TenantId && x.Id == request.DeploymentPackageId)
            .Select(x => new { x.Id, x.Name, x.Version, x.Sha256, SizeBytes = x.Content.Length })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Deployment package not found in this tenant.");

        var change = await _db.Tickets.Include(x => x.Approvals)
            .SingleOrDefaultAsync(x => x.TenantId == actor.TenantId && x.Id == request.ChangeTicketId, cancellationToken)
            ?? throw new NotFoundException("Change ticket not found in this tenant.");
        if (change.Type != TicketType.Change)
            throw new ConflictException("A deployment request must be linked to a change ticket.");
        if (change.Status is TicketStatus.Rejected or TicketStatus.Cancelled)
            throw new ConflictException("A rejected or cancelled change ticket cannot authorize a deployment.");
        if (await _db.DeploymentRequests.AnyAsync(x => x.TenantId == actor.TenantId && x.ChangeTicketId == change.Id, cancellationToken))
            throw new ConflictException("A deployment request already uses this change ticket.");

        var now = _clock.UtcNow;
        var deployment = new DeploymentRequest
        {
            TenantId = actor.TenantId,
            TargetAgentId = agent.Id,
            DeploymentPackageId = package.Id,
            PackageName = package.Name,
            PackageVersion = package.Version,
            PackageSha256 = package.Sha256,
            ChangeTicketId = change.Id,
            Environment = change.TargetEnvironment?.Trim() ?? string.Empty,
            GitRepoURL = request.GitRepoURL,
            GitBranch = request.GitBranch,
            TargetPath = request.TargetPath,
            Status = change.Status == TicketStatus.Approved
                ? DeploymentRequestStatus.Approved
                : DeploymentRequestStatus.PendingApproval,
            RequestedByUserId = actor.UserId,
            RequestedBySubject = actor.Subject,
            RequestedByDisplay = actor.DisplayName,
            RequestedAt = now,
            CreatedAt = now
        };
        _db.DeploymentRequests.Add(deployment);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "deployment.requested", "DeploymentRequest", deployment.Id.ToString(), "success",
            new { deployment.TargetAgentId, deployment.DeploymentPackageId, deployment.PackageSha256, deployment.ChangeTicketId, deployment.Environment, deployment.GitRepoURL },
            actor.IpAddress, cancellationToken);
        await _realtime.DeploymentChangedAsync(actor.TenantId, deployment.Id, cancellationToken);

        return new DeploymentRequestDto(deployment.Id, agent.Id, agent.Hostname, package.Id, package.Name, package.Version,
            package.Sha256, package.SizeBytes, change.Id, change.Number, deployment.Environment,
            deployment.GitRepoURL, deployment.GitBranch, deployment.TargetPath, change.Status.ToString(),
            change.RequiredApprovals, change.Approvals.Count, actor.UserId, actor.DisplayName, now);
    }

    public async Task UpdateDeploymentResultAsync(Guid tenantId, Guid agentId, Guid deploymentId, string status, string? detail, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var deployment = await _db.DeploymentRequests
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == deploymentId && d.TargetAgentId == agentId, cancellationToken)
            ?? throw new NotFoundException("Deployment request was not found for this agent.");

        if (deployment.Status != DeploymentRequestStatus.Executing)
        {
            throw new ConflictException("Only a deployment that the agent has started can report a result.");
        }

        var newStatus = status switch
        {
            "succeeded" => DeploymentRequestStatus.Succeeded,
            "failed" => DeploymentRequestStatus.Failed,
            _ => throw new ValidationException("Invalid deployment status.")
        };

        var now = _clock.UtcNow;
        deployment.Status = newStatus;
        deployment.UpdatedAt = now;
        var nonces = await _db.DeploymentManifestNonces
            .Where(n => n.TenantId == tenantId && n.DeploymentId == deploymentId && !n.Used)
            .ToListAsync(cancellationToken);
        foreach (var nonce in nonces)
        {
            nonce.Used = true;
            nonce.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(tenantId, AuditActorType.Agent, agentId, null,
            "deployment.result_reported", "DeploymentRequest", deploymentId.ToString(), newStatus.ToString(),
            new { Status = newStatus.ToString(), Detail = Truncate(detail, 4000) }, null, cancellationToken);
        await _realtime.DeploymentChangedAsync(tenantId, deploymentId, cancellationToken);
    }

    public async Task<IReadOnlyList<DeploymentRequestDto>> PendingDeploymentsForAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        // Tenant context must be set from the agent's token in the handler
        var rows = await _db.DeploymentRequests.AsNoTracking()
            .Where(d => d.TargetAgentId == agentId && d.Status == DeploymentRequestStatus.Approved)
            .OrderBy(d => d.RequestedAt)
            .Take(50)
            .Select(d => new
            {
                d.Id, d.PackageName, d.PackageVersion, d.PackageSha256,
                d.GitRepoURL, d.GitBranch, d.TargetPath, d.Environment,
                d.TargetAgentId, d.ChangeTicketId, d.RequestedAt
            })
            .ToArrayAsync(cancellationToken);
        return rows.Select(r => new DeploymentRequestDto(
            r.Id, r.TargetAgentId, "", Guid.Empty, r.PackageName, r.PackageVersion, r.PackageSha256, 0,
            r.ChangeTicketId, "", r.Environment, r.GitRepoURL, r.GitBranch, r.TargetPath,
            DeploymentRequestStatus.Approved.ToString(), 0, 0, null, "", r.RequestedAt)).ToArray();
    }

    public async Task<DeploymentManifestDto> GetManifestAsync(Guid agentId, Guid deploymentId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new ForbiddenException("The agent has no tenant scope.");

        if (!_options.ExecutionEnabled)
        {
            throw new ConflictException("Deployment execution is disabled by server policy.");
        }

        if (!_manifest.IsConfigured)
        {
            throw new ConflictException("Deployment manifest signing is not configured; execution is unavailable.");
        }

        var deployment = await _db.DeploymentRequests
            .Include(d => d.TargetAgent)
            .Include(d => d.ChangeTicket)
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == deploymentId && d.TargetAgentId == agentId, cancellationToken)
            ?? throw new NotFoundException("Deployment request was not found for this agent.");

        if (deployment.TargetAgent.Status != AgentStatus.Active)
        {
            throw new ForbiddenException("The agent is not active.");
        }

        if (!deployment.TargetAgent.DeploymentExecutionAllowed)
        {
            throw new ForbiddenException("Deployment execution was not granted to this agent.");
        }

        if (deployment.ChangeTicket.Status != TicketStatus.Approved)
        {
            throw new ConflictException("The linked change ticket is not approved.");
        }

        if (deployment.Status is not (DeploymentRequestStatus.Approved or DeploymentRequestStatus.Executing))
        {
            throw new ConflictException($"A deployment in state {deployment.Status} cannot receive work.");
        }

        var targetPath = deployment.TargetPath?.Trim();
        if (string.IsNullOrWhiteSpace(targetPath)
            || targetPath.Contains('\0')
            || targetPath.Contains('\n')
            || targetPath.Contains('\r'))
        {
            throw new ConflictException("A safe, non-empty target path is required before execution.");
        }

        var now = _clock.UtcNow;
        var nonce = await _db.DeploymentManifestNonces
            .Where(n => n.TenantId == tenantId && n.DeploymentId == deploymentId && !n.Used && n.ExpiresAt > now)
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var issued = false;
        if (nonce is null)
        {
            if (await _db.DeploymentManifestNonces.AnyAsync(n => n.TenantId == tenantId && n.DeploymentId == deploymentId && n.Used, cancellationToken))
            {
                throw new ConflictException("This deployment manifest has already been consumed.");
            }

            nonce = new DeploymentManifestNonce
            {
                TenantId = tenantId,
                DeploymentId = deploymentId,
                Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
                ExpiresAt = now.AddMinutes(Math.Clamp(_options.ManifestLifetimeMinutes, 1, 1440)),
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.DeploymentManifestNonces.Add(nonce);
            issued = true;
        }

        if (deployment.Status == DeploymentRequestStatus.Approved)
        {
            deployment.Status = DeploymentRequestStatus.Executing;
            deployment.UpdatedAt = now;
            issued = true;
        }

        if (issued)
        {
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.WriteAsync(tenantId, AuditActorType.Agent, agentId, null,
                "deployment.manifest_issued", "DeploymentRequest", deploymentId.ToString(), "success",
                new { nonce.Nonce, nonce.ExpiresAt, deployment.PackageSha256, targetPath }, null, cancellationToken);
            await _realtime.DeploymentChangedAsync(tenantId, deploymentId, cancellationToken);
        }

        return _manifest.SignManifest(deployment.Id, agentId, tenantId, deployment.PackageName,
            deployment.PackageVersion, deployment.PackageSha256, deployment.GitRepoURL, deployment.GitBranch,
            targetPath, nonce.Nonce, nonce.CreatedAt, nonce.ExpiresAt);
    }

    public async Task<DeploymentArtifactDto> GetArtifactAsync(Guid agentId, Guid deploymentId, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new ForbiddenException("The agent has no tenant scope.");

        if (!_options.ExecutionEnabled)
        {
            throw new ConflictException("Deployment execution is disabled by server policy.");
        }

        var deployment = await _db.DeploymentRequests
            .Include(d => d.TargetAgent)
            .Include(d => d.Package)
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == deploymentId && d.TargetAgentId == agentId, cancellationToken)
            ?? throw new NotFoundException("Deployment request was not found for this agent.");

        if (deployment.TargetAgent.Status != AgentStatus.Active || !deployment.TargetAgent.DeploymentExecutionAllowed)
        {
            throw new ForbiddenException("The agent is not authorized to receive deployment artifacts.");
        }

        if (deployment.Status != DeploymentRequestStatus.Executing)
        {
            throw new ConflictException("The deployment manifest must be issued before the artifact can be downloaded.");
        }

        var now = _clock.UtcNow;
        var authorized = await _db.DeploymentManifestNonces.AnyAsync(n =>
            n.TenantId == tenantId && n.DeploymentId == deploymentId && !n.Used && n.ExpiresAt > now, cancellationToken);
        if (!authorized)
        {
            throw new ConflictException("No active deployment manifest authorizes this artifact download.");
        }

        var digest = Convert.ToHexString(SHA256.HashData(deployment.Package.Content)).ToLowerInvariant();
        if (!string.Equals(digest, deployment.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("The stored artifact digest does not match the deployment record.");
        }

        var fileName = $"{SanitizeFileName(deployment.PackageName)}-{SanitizeFileName(deployment.PackageVersion)}.zip";
        return new DeploymentArtifactDto(deployment.Package.Content, fileName, digest);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "package" : sanitized;
    }

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
