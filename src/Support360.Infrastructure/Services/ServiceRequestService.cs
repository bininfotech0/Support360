using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Contracts.Operations;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class ServiceRequestService : IServiceRequestService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IValidator<CreateServiceRequest> _createServiceRequest;
    private readonly IValidator<TransitionServiceRequest> _transitionServiceRequest;

    public ServiceRequestService(
        ISupport360DbContext db,
        ITenantContext tenant,
        IClock clock,
        IAuditService audit,
        IValidator<CreateServiceRequest> createServiceRequest,
        IValidator<TransitionServiceRequest> transitionServiceRequest)
    {
        _db = db; _tenant = tenant; _clock = clock; _audit = audit;
        _createServiceRequest = createServiceRequest; _transitionServiceRequest = transitionServiceRequest;
    }

    public async Task<IReadOnlyList<ServiceRequestDto>> ListServiceRequestsAsync(Guid tenantId, string? status, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var query = _db.ServiceRequests.AsNoTracking().Where(s => s.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ServiceRequestStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
                throw new ValidationException("Unknown service request status.");
            query = query.Where(s => s.Status == parsed);
        }
        var requests = await query.OrderByDescending(s => s.OpenedAt).Take(200).ToListAsync(cancellationToken);
        return requests.Select(MapRequest).ToArray();
    }

    public async Task<ServiceRequestDto> CreateServiceRequestAsync(RequestActor actor, CreateServiceRequest request, CancellationToken cancellationToken)
    {
        await _createServiceRequest.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var now = _clock.UtcNow;
        var serviceRequest = new ServiceRequest
        {
            Id = Guid.NewGuid(), TenantId = actor.TenantId, Number = MakeNumber("SRV", now, Guid.NewGuid()),
            Title = request.Title.Trim(), Description = request.Description.Trim(), Category = request.Category.Trim(),
            RequestedBySubject = actor.Subject, RequestedByDisplay = actor.DisplayName, RequestedByUserId = actor.UserId,
            Status = ServiceRequestStatus.New, OpenedAt = now, CreatedAt = now
        };
        _db.ServiceRequests.Add(serviceRequest);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "service_request.created", "ServiceRequest", serviceRequest.Id.ToString(), "success",
            new { serviceRequest.Number, serviceRequest.Category }, actor.IpAddress, cancellationToken);
        return MapRequest(serviceRequest);
    }

    public async Task<ServiceRequestDto?> TransitionServiceRequestAsync(RequestActor actor, Guid serviceRequestId, TransitionServiceRequest request, CancellationToken cancellationToken)
    {
        await _transitionServiceRequest.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var serviceRequest = await _db.ServiceRequests
            .SingleOrDefaultAsync(s => s.TenantId == actor.TenantId && s.Id == serviceRequestId, cancellationToken);
        if (serviceRequest is null) return null;
        var next = Enum.Parse<ServiceRequestStatus>(request.Status, true);
        if (!CanTransition(serviceRequest.Status, next)) throw new ConflictException($"Service request cannot move from {serviceRequest.Status} to {next}.");
        if (request.AssignedToUserId is Guid assignee
            && !await _db.Users.AnyAsync(u => u.TenantId == actor.TenantId && u.Id == assignee && u.IsActive, cancellationToken))
            throw new NotFoundException("Assignee was not found in this tenant.");
        var previous = serviceRequest.Status;
        var now = _clock.UtcNow;
        serviceRequest.Status = next;
        serviceRequest.AssignedToUserId = request.AssignedToUserId;
        if (next == ServiceRequestStatus.Acknowledged) serviceRequest.AcknowledgedAt = now;
        if (next == ServiceRequestStatus.Resolved) serviceRequest.ResolvedAt = now;
        if (next == ServiceRequestStatus.Closed) serviceRequest.ClosedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "service_request.status_changed", "ServiceRequest", serviceRequest.Id.ToString(), "success",
            new { Previous = previous.ToString(), Current = next.ToString(), serviceRequest.AssignedToUserId }, actor.IpAddress, cancellationToken);
        return MapRequest(serviceRequest);
    }

    private static bool CanTransition(ServiceRequestStatus from, ServiceRequestStatus to) => (from, to) switch
    {
        (ServiceRequestStatus.New, ServiceRequestStatus.Acknowledged or ServiceRequestStatus.InProgress or ServiceRequestStatus.Resolved) => true,
        (ServiceRequestStatus.Acknowledged, ServiceRequestStatus.InProgress or ServiceRequestStatus.Resolved) => true,
        (ServiceRequestStatus.InProgress, ServiceRequestStatus.Resolved) => true,
        (ServiceRequestStatus.Resolved, ServiceRequestStatus.Closed) => true,
        _ => false
    };

    private static string MakeNumber(string prefix, DateTimeOffset now, Guid id) => $"{prefix}-{now:yyyyMMdd}-{id:N}"[..(prefix.Length + 10 + 9)];

    private static ServiceRequestDto MapRequest(ServiceRequest request) => new(
        request.Id, request.Number, request.Title, request.Description, request.Category,
        request.Status.ToString(), request.RequestedByUserId, request.RequestedByDisplay,
        request.AssignedToUserId, request.OpenedAt, request.AcknowledgedAt, request.ResolvedAt, request.ClosedAt);
}
