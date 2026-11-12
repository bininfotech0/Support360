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

    [HttpGet("{id:guid}/manifest")]
    public async Task<ActionResult<DeploymentManifestDto>> Manifest(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAgentId(out var agentId))
            return Unauthorized();

        var manifest = await _deployments.GetManifestAsync(agentId, id, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(manifest);
    }

    [HttpGet("{id:guid}/artifact")]
    public async Task<IActionResult> Artifact(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetAgentId(out var agentId))
            return Unauthorized();

        var artifact = await _deployments.GetArtifactAsync(agentId, id, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return File(artifact.Content, "application/zip", artifact.FileName);
    }

    [HttpPost("result")]
    public async Task<ActionResult> ReportResult([FromBody] DeploymentResultReport request, CancellationToken cancellationToken)
    {
        if (!TryGetAgentId(out var agentId) || !TryGetTenantId(out var tenantId))
            return Unauthorized();

        var deploymentId = request.DeploymentId;
        if (deploymentId == Guid.Empty) return BadRequest("Deployment ID is required.");

        await _deployments.UpdateDeploymentResultAsync(tenantId, agentId, deploymentId, request.Status, request.Detail, cancellationToken);
        return Ok();
    }

    private bool TryGetAgentId(out Guid agentId) =>
        Guid.TryParse(User.FindFirst("agent_id")?.Value, out agentId);

    private bool TryGetTenantId(out Guid tenantId) =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out tenantId);
}

public sealed record DeploymentResultReport(Guid DeploymentId, string Status, string? Detail);
