using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Support360.Api.Auth;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Contracts.Applications;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/agent/v1/application-checks")]
[Authorize(AuthenticationSchemes = AgentKeyAuthenticationHandler.SchemeName)]
[EnableRateLimiting("agent")]
public sealed class AgentApplicationChecksController : ControllerBase
{
    private readonly IApplicationCheckService _checks;

    public AgentApplicationChecksController(IApplicationCheckService checks) => _checks = checks;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AgentApplicationCheckDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _checks.GetAgentChecksAsync(RequireIdentity(), cancellationToken));

    [HttpPost("results")]
    public async Task<ActionResult<ApplicationCheckResultsResponse>> Results(
        ApplicationCheckResultsRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _checks.IngestResultsAsync(RequireIdentity(), request, cancellationToken));

    private AgentIdentity RequireIdentity()
    {
        if (!Guid.TryParse(User.FindFirst("agent_id")?.Value, out var agentId)
            || !Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var tenantId))
        {
            throw new InvalidCredentialException("Agent identity is incomplete.");
        }

        return new AgentIdentity(agentId, tenantId);
    }
}
