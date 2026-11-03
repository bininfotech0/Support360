using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Contracts.Operations;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class IncidentChangeService : IIncidentChangeService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IRealtimeNotifier _realtime;
    private readonly IValidator<CreateIncidentRequest> _createIncident;
    private readonly IValidator<TransitionIncidentRequest> _transitionIncident;
    private readonly IValidator<CreateChangeRequest> _createChange;
    private readonly IValidator<DecideChangeRequest> _decideChange;
    private readonly IValidator<UpdateChangeApprovalPolicyRequest> _policyValidator;

    public IncidentChangeService(
        ISupport360DbContext db,
        ITenantContext tenant,
        IClock clock,
        IAuditService audit,
        IRealtimeNotifier realtime,
        IValidator<CreateIncidentRequest> createIncident,
        IValidator<TransitionIncidentRequest> transitionIncident,
        IValidator<CreateChangeRequest> createChange,
        IValidator<DecideChangeRequest> decideChange,
        IValidator<UpdateChangeApprovalPolicyRequest> policyValidator)
    {
        _db = db; _tenant = tenant; _clock = clock; _audit = audit; _realtime = realtime;
        _createIncident = createIncident; _transitionIncident = transitionIncident;
        _createChange = createChange; _decideChange = decideChange; _policyValidator = policyValidator;
    }

    public async Task<IReadOnlyList<IncidentDto>> ListIncidentsAsync(Guid tenantId, string? status, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var query = _db.Incidents.AsNoTracking().Where(i => i.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<IncidentStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
                throw new ValidationException("Unknown incident status.");
            query = query.Where(i => i.Status == parsed);
        }
        var incidents = await query.Include(i => i.Alerts).ThenInclude(a => a.Alert)
            .OrderByDescending(i => i.OpenedAt).Take(200).ToListAsync(cancellationToken);
        var ids = incidents.Select(i => i.Id).ToArray();
        var related = await _db.ChangeRequests.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.RelatedIncidentId != null && ids.Contains(c.RelatedIncidentId.Value))
            .Select(c => new { c.Id, IncidentId = c.RelatedIncidentId!.Value }).ToListAsync(cancellationToken);
        var byIncident = related.GroupBy(x => x.IncidentId).ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.Id).ToArray());
        return incidents.Select(i => MapIncident(i, byIncident.GetValueOrDefault(i.Id) ?? [])).ToArray();
    }

    public async Task<IncidentDto> CreateIncidentAsync(RequestActor actor, CreateIncidentRequest request, CancellationToken cancellationToken)
    {
        await _createIncident.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var alertIds = request.AlertIds ?? Array.Empty<Guid>();
        List<Alert> alerts = alertIds.Count == 0
            ? []
            : await _db.Alerts.Where(a => a.TenantId == actor.TenantId && alertIds.Contains(a.Id)).ToListAsync(cancellationToken);
        if (alerts.Count != alertIds.Count) throw new NotFoundException("One or more alerts were not found in this tenant.");

        if (request.RelatedBatchJobExecutionId is Guid executionId
            && !await _db.BatchJobExecutions.AnyAsync(e => e.TenantId == actor.TenantId && e.Id == executionId, cancellationToken))
            throw new NotFoundException("Related batch-job execution was not found in this tenant.");

        var now = _clock.UtcNow;
        var incident = new Incident
        {
            Id = Guid.NewGuid(), TenantId = actor.TenantId, Number = MakeNumber("INC", now, Guid.NewGuid()),
            Title = request.Title.Trim(), Description = request.Description.Trim(),
            Severity = Enum.Parse<IncidentSeverity>(request.Severity, true), Status = IncidentStatus.New,
            CreatedByUserId = actor.UserId, RelatedBatchJobExecutionId = request.RelatedBatchJobExecutionId,
            OpenedAt = now, CreatedAt = now,
            Alerts = alerts.Select(a => new IncidentAlert
            {
                TenantId = actor.TenantId, AlertId = a.Id, LinkedAt = now, LinkedByUserId = actor.UserId, CreatedAt = now
            }).ToList()
        };
        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "incident.created", "Incident", incident.Id.ToString(), "success",
            new { incident.Number, incident.Severity, AlertIds = alertIds, incident.RelatedBatchJobExecutionId }, actor.IpAddress, cancellationToken);
        return MapIncident(incident, []);
    }

    public async Task<IncidentDto?> TransitionIncidentAsync(RequestActor actor, Guid incidentId, TransitionIncidentRequest request, CancellationToken cancellationToken)
    {
        await _transitionIncident.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var incident = await _db.Incidents.Include(i => i.Alerts).ThenInclude(a => a.Alert)
            .SingleOrDefaultAsync(i => i.TenantId == actor.TenantId && i.Id == incidentId, cancellationToken);
        if (incident is null) return null;
        var next = Enum.Parse<IncidentStatus>(request.Status, true);
        if (!CanTransition(incident.Status, next)) throw new ConflictException($"Incident cannot move from {incident.Status} to {next}.");
        if (request.AssignedToUserId is Guid assignee
            && !await _db.Users.AnyAsync(u => u.TenantId == actor.TenantId && u.Id == assignee && u.IsActive, cancellationToken))
            throw new NotFoundException("Assignee was not found in this tenant.");
        var previous = incident.Status;
        var now = _clock.UtcNow;
        incident.Status = next;
        incident.AssignedToUserId = request.AssignedToUserId;
        if (next == IncidentStatus.Acknowledged) incident.AcknowledgedAt = now;
        if (next == IncidentStatus.Resolved) incident.ResolvedAt = now;
        if (next == IncidentStatus.Closed) incident.ClosedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "incident.status_changed", "Incident", incident.Id.ToString(), "success",
            new { Previous = previous.ToString(), Current = next.ToString(), incident.AssignedToUserId }, actor.IpAddress, cancellationToken);
        var relatedIds = await _db.ChangeRequests.AsNoTracking().Where(c => c.TenantId == actor.TenantId && c.RelatedIncidentId == incident.Id)
            .Select(c => c.Id).ToListAsync(cancellationToken);
        return MapIncident(incident, relatedIds);
    }

    public async Task<IReadOnlyList<ChangeRequestDto>> ListChangeRequestsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var requests = await _db.ChangeRequests.AsNoTracking().Include(c => c.Approvals)
            .Where(c => c.TenantId == tenantId).OrderByDescending(c => c.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return requests.Select(MapChange).ToArray();
    }

    public async Task<ChangeRequestDto> CreateChangeRequestAsync(RequestActor actor, CreateChangeRequest request, CancellationToken cancellationToken)
    {
        await _createChange.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var policy = await _db.Tenants.SingleOrDefaultAsync(t => t.Id == actor.TenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant not found.");
        if (request.RelatedIncidentId is Guid incidentId
            && !await _db.Incidents.AnyAsync(i => i.TenantId == actor.TenantId && i.Id == incidentId && i.Status != IncidentStatus.Closed, cancellationToken))
            throw new NotFoundException("Open related incident was not found in this tenant.");
        if (string.IsNullOrWhiteSpace(actor.Subject)) throw new ForbiddenException("A stable user subject is required to request a change.");

        var now = _clock.UtcNow;
        var change = new ChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = actor.TenantId, Number = MakeNumber("CHG", now, Guid.NewGuid()),
            Title = request.Title.Trim(), Description = request.Description.Trim(), Risk = request.Risk.Trim(),
            TargetEnvironment = request.TargetEnvironment.Trim(), PlannedStartAt = request.PlannedStartAt.ToUniversalTime(),
            PlannedEndAt = request.PlannedEndAt.ToUniversalTime(), RequiredApprovals = Math.Clamp(policy.RequiredChangeApprovals, 1, 5),
            Status = ChangeRequestStatus.PendingApproval, RequestedByUserId = actor.UserId,
            RequestedBySubject = actor.Subject, RequestedByDisplay = actor.DisplayName,
            ConcurrencyToken = Guid.NewGuid(),
            RelatedIncidentId = request.RelatedIncidentId, CreatedAt = now
        };
        _db.ChangeRequests.Add(change);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "change_request.created", "ChangeRequest", change.Id.ToString(), "success",
            new { change.Number, change.TargetEnvironment, change.PlannedStartAt, change.PlannedEndAt, change.RequiredApprovals, change.RelatedIncidentId }, actor.IpAddress, cancellationToken);
        return MapChange(change);
    }

    public async Task<ChangeRequestDto?> DecideChangeRequestAsync(RequestActor actor, Guid changeRequestId, DecideChangeRequest request, CancellationToken cancellationToken)
    {
        await _decideChange.ValidateAndThrowAsync(request, cancellationToken);
        if (actor.Role is not (UserRole.Approver or UserRole.TenantAdmin or UserRole.PlatformAdmin))
            throw new ForbiddenException("Only an approver or tenant administrator can decide a change request.");
        if (string.IsNullOrWhiteSpace(actor.Subject)) throw new ForbiddenException("A stable user subject is required to approve a change.");
        _tenant.SetTenant(actor.TenantId);
        var change = await _db.ChangeRequests.Include(c => c.Approvals)
            .SingleOrDefaultAsync(c => c.TenantId == actor.TenantId && c.Id == changeRequestId, cancellationToken);
        if (change is null) return null;
        if (change.Status != ChangeRequestStatus.PendingApproval) throw new ConflictException("Only a pending change request can receive a decision.");
        var tenant = await _db.Tenants.AsNoTracking().SingleAsync(t => t.Id == actor.TenantId, cancellationToken);
        if (tenant.RequireChangeSeparationOfDuties && string.Equals(change.RequestedBySubject, actor.Subject, StringComparison.Ordinal))
            throw new ForbiddenException("The requester cannot approve or reject their own change request.");
        if (change.Approvals.Any(a => string.Equals(a.ApproverSubject, actor.Subject, StringComparison.Ordinal)))
            throw new ConflictException("This approver has already decided this change request.");

        var decision = Enum.Parse<ChangeApprovalDecision>(request.Decision, true);
        var approval = new ChangeApproval
        {
            TenantId = actor.TenantId, ChangeRequestId = change.Id, ApproverSubject = actor.Subject,
            ApproverUserId = actor.UserId, ApproverDisplay = actor.DisplayName, Decision = decision,
            Comment = request.Comment?.Trim(), DecidedAt = _clock.UtcNow
        };
        var priorApprovals = change.Approvals.Count(a => a.Decision == ChangeApprovalDecision.Approved);
        _db.ChangeApprovals.Add(approval);
        change.ConcurrencyToken = Guid.NewGuid();
        if (decision == ChangeApprovalDecision.Rejected) change.Status = ChangeRequestStatus.Rejected;
        else if (priorApprovals + 1 >= change.RequiredApprovals)
            change.Status = ChangeRequestStatus.Approved;
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("The change request was updated concurrently. Refresh it before deciding."); }
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            $"change_request.{decision.ToString().ToLowerInvariant()}", "ChangeRequest", change.Id.ToString(), "success",
            new { change.Number, decision, request.Comment, change.Status, change.RequiredApprovals }, actor.IpAddress, cancellationToken);
        await NotifyLinkedDeploymentsAsync(actor.TenantId, change.Id, cancellationToken);
        return MapChange(change);
    }

    public async Task<ChangeRequestDto?> CancelChangeRequestAsync(RequestActor actor, Guid changeRequestId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(actor.TenantId);
        var change = await _db.ChangeRequests.Include(c => c.Approvals)
            .SingleOrDefaultAsync(c => c.TenantId == actor.TenantId && c.Id == changeRequestId, cancellationToken);
        if (change is null) return null;
        if (change.Status != ChangeRequestStatus.PendingApproval) throw new ConflictException("Only a pending change request can be cancelled.");
        var isAdmin = actor.Role is UserRole.TenantAdmin or UserRole.PlatformAdmin;
        if (!isAdmin && !string.Equals(change.RequestedBySubject, actor.Subject, StringComparison.Ordinal))
            throw new ForbiddenException("Only the requester or a tenant administrator can cancel this change request.");
        change.Status = ChangeRequestStatus.Cancelled;
        change.ConcurrencyToken = Guid.NewGuid();
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("The change request was updated concurrently. Refresh it before cancelling."); }
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "change_request.cancelled", "ChangeRequest", change.Id.ToString(), "success",
            new { change.Number }, actor.IpAddress, cancellationToken);
        await NotifyLinkedDeploymentsAsync(actor.TenantId, change.Id, cancellationToken);
        return MapChange(change);
    }

    public async Task<ChangeApprovalPolicyDto> GetPolicyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var tenant = await _db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant not found.");
        return new ChangeApprovalPolicyDto(tenant.RequiredChangeApprovals, tenant.RequireChangeSeparationOfDuties);
    }

    public async Task<ChangeApprovalPolicyDto> UpdatePolicyAsync(RequestActor actor, UpdateChangeApprovalPolicyRequest request, CancellationToken cancellationToken)
    {
        await _policyValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var tenant = await _db.Tenants.SingleOrDefaultAsync(t => t.Id == actor.TenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant not found.");
        tenant.RequiredChangeApprovals = request.RequiredApprovals;
        tenant.RequireChangeSeparationOfDuties = request.RequireSeparationOfDuties;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "change_policy.updated", "Tenant", actor.TenantId.ToString(), "success", request, actor.IpAddress, cancellationToken);
        return new ChangeApprovalPolicyDto(tenant.RequiredChangeApprovals, tenant.RequireChangeSeparationOfDuties);
    }

    private static bool CanTransition(IncidentStatus from, IncidentStatus to) => (from, to) switch
    {
        (IncidentStatus.New, IncidentStatus.Acknowledged or IncidentStatus.InProgress or IncidentStatus.Resolved) => true,
        (IncidentStatus.Acknowledged, IncidentStatus.InProgress or IncidentStatus.Resolved) => true,
        (IncidentStatus.InProgress, IncidentStatus.Resolved) => true,
        (IncidentStatus.Resolved, IncidentStatus.Closed) => true,
        _ => false
    };

    private async Task NotifyLinkedDeploymentsAsync(Guid tenantId, Guid changeRequestId, CancellationToken cancellationToken)
    {
        var deploymentIds = await _db.DeploymentRequests.AsNoTracking()
            .Where(deployment => deployment.TenantId == tenantId && deployment.ChangeRequestId == changeRequestId)
            .Select(deployment => deployment.Id)
            .ToListAsync(cancellationToken);
        foreach (var deploymentId in deploymentIds)
            await _realtime.DeploymentChangedAsync(tenantId, deploymentId, cancellationToken);
    }

    private static string MakeNumber(string prefix, DateTimeOffset now, Guid id) => $"{prefix}-{now:yyyyMMdd}-{id:N}"[..(prefix.Length + 10 + 9)];

    private static IncidentDto MapIncident(Incident incident, IReadOnlyList<Guid> relatedChanges) => new(
        incident.Id, incident.Number, incident.Title, incident.Description, incident.Severity.ToString(), incident.Status.ToString(),
        incident.CreatedByUserId, incident.AssignedToUserId, incident.RelatedBatchJobExecutionId,
        incident.OpenedAt, incident.AcknowledgedAt, incident.ResolvedAt, incident.ClosedAt,
        incident.Alerts.Select(link => new IncidentAlertDto(link.AlertId, link.Alert.Severity.ToString(), link.Alert.Status.ToString(), link.Alert.Message, link.Alert.TriggeredAt)).ToArray(),
        relatedChanges);

    private static ChangeRequestDto MapChange(ChangeRequest change) => new(
        change.Id, change.Number, change.Title, change.Description, change.Risk, change.TargetEnvironment,
        change.PlannedStartAt, change.PlannedEndAt, change.RequiredApprovals, change.Status.ToString(),
        change.RequestedByUserId, change.RequestedByDisplay, change.RelatedIncidentId,
        change.Approvals.OrderBy(a => a.DecidedAt).Select(a => new ChangeApprovalDto(a.ApproverDisplay, a.Decision.ToString(), a.Comment, a.DecidedAt)).ToArray());
}
