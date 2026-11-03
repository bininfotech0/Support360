using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Deployments;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/deployment-packages")]
[Authorize(Policy = "TenantUser")]
public sealed class DeploymentPackagesController : TenantControllerBase
{
    private readonly IDeploymentPackageService _packages;
    public DeploymentPackagesController(IDeploymentPackageService packages) => _packages = packages;

    [HttpGet]
    public Task<IReadOnlyList<DeploymentPackageDto>> List(CancellationToken cancellationToken) =>
        _packages.ListAsync(RequireTenantId(), cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeploymentPackageDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var package = await _packages.GetAsync(RequireTenantId(), id, cancellationToken);
        return package is null ? NotFound() : Ok(package);
    }

    [HttpPost]
    [Authorize(Policy = "TenantAdmin")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 21 * 1024 * 1024)]
    public async Task<ActionResult<DeploymentPackageDto>> Upload(
        [FromForm] string name,
        [FromForm] string version,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length is <= 0 or > DeploymentPackageLimits.MaximumPackageBytes)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid package size", detail: "ZIP packages must be between 1 byte and 20 MiB.");
        if (!string.Equals(Path.GetExtension(file.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid package type", detail: "Only ZIP packages are accepted.");

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(memory, cancellationToken);
        var package = await _packages.UploadAsync(RequireActor(), new UploadDeploymentPackageRequest(name, version, memory.ToArray()), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = package.Id }, package);
    }
}
