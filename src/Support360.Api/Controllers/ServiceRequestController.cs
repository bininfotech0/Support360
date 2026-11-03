using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Operations;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/service-requests")]
[Authorize(Policy = "TenantUser")]
public sealed class ServiceRequestController : TenantControllerBase
{
    private readonly IServiceRequestService _service;
    public ServiceRequestController(IServiceRequestService service) => _service = service;

    [HttpGet]
    public Task<IReadOnlyList<ServiceRequestDto>> List([FromQuery] string? status, CancellationToken cancellationToken) =>
        _service.ListServiceRequestsAsync(RequireTenantId(), status, cancellationToken);

    [HttpPost]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<ServiceRequestDto>> Create(CreateServiceRequest request, CancellationToken cancellationToken)
    {
        var serviceRequest = await _service.CreateServiceRequestAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/service-requests/{serviceRequest.Id}", serviceRequest);
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<ServiceRequestDto>> Transition(Guid id, TransitionServiceRequest request, CancellationToken cancellationToken)
    {
        var serviceRequest = await _service.TransitionServiceRequestAsync(RequireActor(), id, request, cancellationToken);
        return serviceRequest is null ? NotFound() : Ok(serviceRequest);
    }
}
