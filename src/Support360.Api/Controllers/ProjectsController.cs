using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.WorkDna;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize(Policy = "TenantUser")]
public sealed class ProjectsController : TenantControllerBase
{
    private readonly IWorkDnaService _service;

    public ProjectsController(IWorkDnaService service) => _service = service;

    [HttpGet]
    public async Task<IReadOnlyList<ProjectDto>> List(CancellationToken cancellationToken)
    {
        var lookups = await _service.GetLookupsAsync(RequireTenantId(), cancellationToken);
        return lookups.Projects;
    }

    [HttpPost]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await _service.CreateProjectAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/projects/{project.Id}", project);
    }
}
