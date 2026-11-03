using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Operations;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/incidents")]
[Authorize(Policy = "TenantUser")]
public sealed class IncidentsController : TenantControllerBase
{
    private readonly IIncidentChangeService _operations;
    public IncidentsController(IIncidentChangeService operations) => _operations = operations;

    [HttpGet]
    public Task<IReadOnlyList<IncidentDto>> List([FromQuery] string? status, CancellationToken cancellationToken) =>
        _operations.ListIncidentsAsync(RequireTenantId(), status, cancellationToken);

    [HttpPost]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<IncidentDto>> Create(CreateIncidentRequest request, CancellationToken cancellationToken)
    {
        var incident = await _operations.CreateIncidentAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/incidents/{incident.Id}", incident);
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<IncidentDto>> Transition(Guid id, TransitionIncidentRequest request, CancellationToken cancellationToken)
    {
        var incident = await _operations.TransitionIncidentAsync(RequireActor(), id, request, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }
}
