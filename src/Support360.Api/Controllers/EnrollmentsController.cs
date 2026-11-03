using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Agents;

namespace Support360.Api.Controllers;

[Route("api/enrollments")]
[Authorize(Policy = "TenantUser")]
public sealed class EnrollmentsController : TenantControllerBase
{
    private readonly IEnrollmentService _enrollments;

    public EnrollmentsController(IEnrollmentService enrollments)
    {
        _enrollments = enrollments;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EnrollmentListItemDto>>> List(CancellationToken cancellationToken)
    {
        return Ok(await _enrollments.ListEnrollmentsAsync(RequireTenantId(), cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<EnrollmentCreatedResponse>> Create(
        CreateEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _enrollments.CreateEnrollmentAsync(RequireActor(), request, cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return CreatedAtAction(nameof(List), new { id = response.EnrollmentId }, response);
    }
}
