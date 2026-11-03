using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Support360.Api.Auth;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Application.Options;
using Support360.Application.Security;
using Support360.Contracts.Telemetry;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/agent/v1")]
[Authorize(AuthenticationSchemes = AgentKeyAuthenticationHandler.SchemeName)]
[EnableRateLimiting("agent")]
public sealed class AgentTelemetryController : ControllerBase
{
    private readonly ITelemetryIngestionService _ingestion;
    private readonly AgentPlatformOptions _options;
    private readonly DeploymentOptions _deploymentOptions;

    public AgentTelemetryController(
        ITelemetryIngestionService ingestion,
        IOptions<AgentPlatformOptions> options,
        IOptions<DeploymentOptions> deploymentOptions)
    {
        _ingestion = ingestion;
        _options = options.Value;
        _deploymentOptions = deploymentOptions.Value;
    }

    [HttpGet("config")]
    public ActionResult<AgentConfigResponse> Config()
    {
        return Ok(new AgentConfigResponse(
            _options.HeartbeatIntervalSeconds,
            _options.TelemetryIntervalSeconds,
            MetricCatalog.Allowed,
            _deploymentOptions.ExecutionEnabled,
            DateTimeOffset.UtcNow));
    }

    [HttpPost("heartbeat")]
    public async Task<ActionResult<HeartbeatResponse>> Heartbeat(
        HeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await _ingestion.HeartbeatAsync(RequireIdentity(), request, cancellationToken));
    }

    [HttpPost("telemetry")]
    public async Task<ActionResult<TelemetryIngestResponse>> Telemetry(
        TelemetryBatchRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await _ingestion.IngestTelemetryAsync(RequireIdentity(), request, cancellationToken));
    }

    [HttpPost("job-events")]
    public async Task<ActionResult<JobEventsResponse>> JobEvents(
        JobEventBatchRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await _ingestion.IngestJobEventsAsync(RequireIdentity(), request, cancellationToken));
    }

    private AgentIdentity RequireIdentity()
    {
        var agentClaim = User.FindFirst("agent_id")?.Value;
        var tenantClaim = User.FindFirst("tenant_id")?.Value;
        if (!Guid.TryParse(agentClaim, out var agentId) || !Guid.TryParse(tenantClaim, out var tenantId))
        {
            throw new InvalidCredentialException("Agent identity is incomplete.");
        }

        return new AgentIdentity(agentId, tenantId);
    }
}
