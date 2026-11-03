using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
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

    public DeploymentService(ISupport360DbContext db, ITenantContext tenantContext, IClock clock,
        IAuditService audit, IRealtimeNotifier realtime, IValidator<CreateDeploymentRequest> validator)
    {
        _db = db; _tenantContext = tenantContext; _clock = clock; _audit = audit; _realtime = realtime; _validator = validator;
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
                PackageSizeBytes = deployment.Package.Content.Length, deployment.ChangeRequestId,
                ChangeRequestNumber = deployment.ChangeRequest.Number, deployment.Environment,
                deployment.GitRepoURL, deployment.GitBranch, deployment.TargetPath,
                deployment.Status, ChangeStatus = deployment.ChangeRequest.Status,
                deployment.ChangeRequest.RequiredApprovals,
                ReceivedApprovals = deployment.ChangeRequest.Approvals.Count, deployment.RequestedByUserId,
                deployment.RequestedByDisplay, deployment.RequestedAt
            })
            .ToArrayAsync(cancellationToken);
        return rows.Select(deployment => new DeploymentRequestDto(
            deployment.Id, deployment.TargetAgentId, deployment.TargetHostname, deployment.DeploymentPackageId,
            deployment.PackageName, deployment.PackageVersion, deployment.PackageSha256, deployment.PackageSizeBytes,
            deployment.ChangeRequestId, deployment.ChangeRequestNumber, deployment.Environment,
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
                PackageSizeBytes = deployment.Package.Content.Length, deployment.ChangeRequestId,
                ChangeRequestNumber = deployment.ChangeRequest.Number, deployment.Environment,
                deployment.GitRepoURL, deployment.GitBranch, deployment.TargetPath,
                deployment.Status, ChangeStatus = deployment.ChangeRequest.Status,
                deployment.ChangeRequest.RequiredApprovals,
                ReceivedApprovals = deployment.ChangeRequest.Approvals.Count, deployment.RequestedByUserId,
                deployment.RequestedByDisplay, deployment.RequestedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new DeploymentRequestDto(
            row.Id, row.TargetAgentId, row.TargetHostname, row.DeploymentPackageId, row.PackageName, row.PackageVersion,
            row.PackageSha256, row.PackageSizeBytes, row.ChangeRequestId, row.ChangeRequestNumber, row.Environment,
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

        var change = await _db.ChangeRequests.Include(x => x.Approvals)
            .SingleOrDefaultAsync(x => x.TenantId == actor.TenantId && x.Id == request.ChangeRequestId, cancellationToken)
            ?? throw new NotFoundException("Change request not found in this tenant.");
        if (change.Status is ChangeRequestStatus.Rejected or ChangeRequestStatus.Cancelled)
            throw new ConflictException("A rejected or cancelled change request cannot authorize a deployment.");
        if (await _db.DeploymentRequests.AnyAsync(x => x.TenantId == actor.TenantId && x.ChangeRequestId == change.Id, cancellationToken))
            throw new ConflictException("A deployment request already uses this change request.");

        var now = _clock.UtcNow;
        var deployment = new DeploymentRequest
        {
            TenantId = actor.TenantId,
            TargetAgentId = agent.Id,
            DeploymentPackageId = package.Id,
            PackageName = package.Name,
            PackageVersion = package.Version,
            PackageSha256 = package.Sha256,
            ChangeRequestId = change.Id,
            Environment = change.TargetEnvironment.Trim(),
            GitRepoURL = request.GitRepoURL,
            GitBranch = request.GitBranch,
            TargetPath = request.TargetPath,
            Status = change.Status == ChangeRequestStatus.Approved
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
            new { deployment.TargetAgentId, deployment.DeploymentPackageId, deployment.PackageSha256, deployment.ChangeRequestId, deployment.Environment, deployment.GitRepoURL },
            actor.IpAddress, cancellationToken);
        await _realtime.DeploymentChangedAsync(actor.TenantId, deployment.Id, cancellationToken);

        return new DeploymentRequestDto(deployment.Id, agent.Id, agent.Hostname, package.Id, package.Name, package.Version,
            package.Sha256, package.SizeBytes, change.Id, change.Number, deployment.Environment,
            deployment.GitRepoURL, deployment.GitBranch, deployment.TargetPath, change.Status.ToString(),
            change.RequiredApprovals, change.Approvals.Count, actor.UserId, actor.DisplayName, now);
    }

    public async Task UpdateDeploymentResultAsync(Guid tenantId, Guid deploymentId, string status, string? detail, CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var deployment = await _db.DeploymentRequests
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == deploymentId, cancellationToken)
            ?? throw new NotFoundException("Deployment request not found.");
        var newStatus = status switch
        {
            "succeeded" => DeploymentRequestStatus.Succeeded,
            "failed" => DeploymentRequestStatus.Failed,
            _ => throw new ValidationException("Invalid deployment status.")
        };
        deployment.Status = newStatus;
        deployment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
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
                d.TargetAgentId, d.ChangeRequestId, d.RequestedAt
            })
            .ToArrayAsync(cancellationToken);
        return rows.Select(r => new DeploymentRequestDto(
            r.Id, r.TargetAgentId, "", Guid.Empty, r.PackageName, r.PackageVersion, r.PackageSha256, 0,
            r.ChangeRequestId, "", r.Environment, r.GitRepoURL, r.GitBranch, r.TargetPath,
            DeploymentRequestStatus.Approved.ToString(), 0, 0, null, "", r.RequestedAt)).ToArray();
    }
}
