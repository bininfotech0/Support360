using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Support360.Application.Abstractions;
using Support360.Contracts.Agents;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/agent/v1")]
[AllowAnonymous]
public sealed class AgentEnrollmentController : ControllerBase
{
    private readonly IEnrollmentService _enrollments;

    public AgentEnrollmentController(IEnrollmentService enrollments)
    {
        _enrollments = enrollments;
    }

    [HttpPost("enroll")]
    [EnableRateLimiting("enroll")]
    public async Task<ActionResult<EnrollAgentResponse>> Enroll(
        EnrollAgentRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _enrollments.EnrollAsync(
            request,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(response);
    }
}
