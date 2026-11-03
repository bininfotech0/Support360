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
                ChangeRequestNumber = deployment.ChangeRequest.Number, deployment.Environment, deployment.Status,
                ChangeStatus = deployment.ChangeRequest.Status, deployment.ChangeRequest.RequiredApprovals,
                ReceivedApprovals = deployment.ChangeRequest.Approvals.Count, deployment.RequestedByUserId,
                deployment.RequestedByDisplay, deployment.RequestedAt
            })
            .ToArrayAsync(cancellationToken);
        return rows.Select(deployment => new DeploymentRequestDto(
            deployment.Id, deployment.TargetAgentId, deployment.TargetHostname, deployment.DeploymentPackageId,
            deployment.PackageName, deployment.PackageVersion, deployment.PackageSha256, deployment.PackageSizeBytes,
            deployment.ChangeRequestId, deployment.ChangeRequestNumber, deployment.Environment,
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
                ChangeRequestNumber = deployment.ChangeRequest.Number, deployment.Environment, deployment.Status,
                ChangeStatus = deployment.ChangeRequest.Status, deployment.ChangeRequest.RequiredApprovals,
                ReceivedApprovals = deployment.ChangeRequest.Approvals.Count, deployment.RequestedByUserId,
                deployment.RequestedByDisplay, deployment.RequestedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new DeploymentRequestDto(
            row.Id, row.TargetAgentId, row.TargetHostname, row.DeploymentPackageId, row.PackageName, row.PackageVersion,
            row.PackageSha256, row.PackageSizeBytes, row.ChangeRequestId, row.ChangeRequestNumber, row.Environment,
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
            new { deployment.TargetAgentId, deployment.DeploymentPackageId, deployment.PackageSha256, deployment.ChangeRequestId, deployment.Environment },
            actor.IpAddress, cancellationToken);
        await _realtime.DeploymentChangedAsync(actor.TenantId, deployment.Id, cancellationToken);

        return new DeploymentRequestDto(deployment.Id, agent.Id, agent.Hostname, package.Id, package.Name, package.Version,
            package.Sha256, package.SizeBytes, change.Id, change.Number, deployment.Environment, change.Status.ToString(),
            change.RequiredApprovals, change.Approvals.Count, actor.UserId, actor.DisplayName, now);
    }

}
