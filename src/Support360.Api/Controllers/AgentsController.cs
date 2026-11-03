using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Contracts.Agents;

namespace Support360.Api.Controllers;

[Route("api/agents")]
[Authorize(Policy = "TenantUser")]
public sealed class AgentsController : TenantControllerBase
{
    private readonly IAgentService _agents;

    public AgentsController(IAgentService agents)
    {
        _agents = agents;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentSummaryDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _agents.ListAgentsAsync(RequireTenantId(), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgentDetailResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var agent = await _agents.GetAgentAsync(RequireTenantId(), id, cancellationToken)
                    ?? throw new NotFoundException("Agent not found.");
        return Ok(agent);
    }

    [HttpGet("{id:guid}/metrics")]
    public async Task<ActionResult<AgentMetricsResponse>> Metrics(
        Guid id,
        [FromQuery] string range = "1h",
        CancellationToken cancellationToken = default)
    {
        return Ok(await _agents.GetMetricsAsync(RequireTenantId(), id, range, cancellationToken));
    }

    [HttpPost("{id:guid}/rotate-credential")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<RotateCredentialResponse>> Rotate(Guid id, CancellationToken cancellationToken)
    {
        var response = await _agents.RotateCredentialAsync(RequireActor(), id, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(response);
    }

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<RevokeAgentResponse>> Revoke(Guid id, CancellationToken cancellationToken)
    {
        var response = await _agents.RevokeAgentAsync(RequireActor(), id, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(response);
    }
}
