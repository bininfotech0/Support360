using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Deployments;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/deployments")]
[Authorize(Policy = "TenantUser")]
public sealed class DeploymentsController : TenantControllerBase
{
    private readonly IDeploymentService _deployments;
    public DeploymentsController(IDeploymentService deployments) => _deployments = deployments;

    [HttpGet]
    public Task<IReadOnlyList<DeploymentRequestDto>> List(CancellationToken cancellationToken) =>
        _deployments.ListAsync(RequireTenantId(), cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeploymentRequestDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var deployment = await _deployments.GetAsync(RequireTenantId(), id, cancellationToken);
        return deployment is null ? NotFound() : Ok(deployment);
    }

    [HttpPost]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<DeploymentRequestDto>> Create(CreateDeploymentRequest request, CancellationToken cancellationToken)
    {
        var deployment = await _deployments.CreateAsync(RequireActor(), request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = deployment.Id }, deployment);
    }
}
