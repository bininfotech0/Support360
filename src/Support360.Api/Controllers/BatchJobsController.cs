using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Dashboard;
using Support360.Contracts.Jobs;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/batch-jobs")]
[Authorize(Policy = "TenantUser")]
public sealed class BatchJobsController : TenantControllerBase
{
    private readonly IDashboardService _dashboard;
    private readonly IBatchJobService _jobs;

    public BatchJobsController(IDashboardService dashboard, IBatchJobService jobs)
    {
        _dashboard = dashboard;
        _jobs = jobs;
    }

    [HttpGet]
    public Task<IReadOnlyList<BatchJobDto>> List(CancellationToken cancellationToken) =>
        _dashboard.ListBatchJobsAsync(RequireTenantId(), cancellationToken);

    [HttpPost]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<BatchJobDto>> Create(ConfigureBatchJobRequest request, CancellationToken cancellationToken)
    {
        var job = await _jobs.CreateAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/batch-jobs/{job.Id}", job);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<BatchJobDto>> Update(Guid id, UpdateBatchJobRequest request, CancellationToken cancellationToken)
    {
        var job = await _jobs.UpdateAsync(RequireActor(), id, request, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }
}
