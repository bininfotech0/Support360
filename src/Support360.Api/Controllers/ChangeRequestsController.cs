using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Operations;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/change-requests")]
[Authorize(Policy = "TenantUser")]
public sealed class ChangeRequestsController : TenantControllerBase
{
    private readonly IIncidentChangeService _operations;
    public ChangeRequestsController(IIncidentChangeService operations) => _operations = operations;

    [HttpGet]
    public Task<IReadOnlyList<ChangeRequestDto>> List(CancellationToken cancellationToken) =>
        _operations.ListChangeRequestsAsync(RequireTenantId(), cancellationToken);

    [HttpPost]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<ChangeRequestDto>> Create(CreateChangeRequest request, CancellationToken cancellationToken)
    {
        var change = await _operations.CreateChangeRequestAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/change-requests/{change.Id}", change);
    }

    [HttpPost("{id:guid}/decision")]
    [Authorize(Policy = "Approver")]
    public async Task<ActionResult<ChangeRequestDto>> Decide(Guid id, DecideChangeRequest request, CancellationToken cancellationToken)
    {
        var change = await _operations.DecideChangeRequestAsync(RequireActor(), id, request, cancellationToken);
        return change is null ? NotFound() : Ok(change);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<ChangeRequestDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var change = await _operations.CancelChangeRequestAsync(RequireActor(), id, cancellationToken);
        return change is null ? NotFound() : Ok(change);
    }
}
