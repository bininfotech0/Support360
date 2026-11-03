using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Api.Auth;
using Support360.Application.Abstractions;
using Support360.Contracts.Deployments;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/agent/v1/deployments")]
[Authorize(AuthenticationSchemes = AgentKeyAuthenticationHandler.SchemeName)]
public sealed class AgentDeploymentController : ControllerBase
{
    private readonly IDeploymentService _deployments;

    public AgentDeploymentController(IDeploymentService deployments) => _deployments = deployments;

    [HttpGet("pending")]
    public async Task<ActionResult<IReadOnlyList<DeploymentRequestDto>>> Pending(CancellationToken cancellationToken)
    {
        var agentClaim = User.FindFirst("agent_id")?.Value;
        if (!Guid.TryParse(agentClaim, out var agentId))
            return Unauthorized();
        var deployments = await _deployments.PendingDeploymentsForAgentAsync(agentId, cancellationToken);
        return Ok(deployments);
    }

    [HttpPost("result")]
    public async Task<ActionResult> ReportResult([FromBody] DeploymentResultReport request, CancellationToken cancellationToken)
    {
        var agentClaim = User.FindFirst("agent_id")?.Value;
        var tenantClaim = User.FindFirst("tenant_id")?.Value;
        if (!Guid.TryParse(agentClaim, out var agentId) || !Guid.TryParse(tenantClaim, out var tenantId))
            return Unauthorized();

        var deploymentId = request.DeploymentId;
        if (deploymentId == Guid.Empty) return BadRequest("Deployment ID is required.");

        await _deployments.UpdateDeploymentResultAsync(tenantId, deploymentId, request.Status, request.Detail, cancellationToken);
        return Ok();
    }
}

public sealed record DeploymentResultReport(Guid DeploymentId, string Status, string? Detail);
