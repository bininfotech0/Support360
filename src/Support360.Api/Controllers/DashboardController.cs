using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Dashboard;

namespace Support360.Api.Controllers;

[Route("api")]
[Authorize(Policy = "TenantUser")]
public sealed class DashboardController : TenantControllerBase
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [HttpGet("overview")]
    public async Task<ActionResult<OverviewDto>> Overview(CancellationToken cancellationToken)
    {
        return Ok(await _dashboard.GetOverviewAsync(RequireTenantId(), cancellationToken));
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<IReadOnlyList<AlertDto>>> Alerts(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        return Ok(await _dashboard.ListAlertsAsync(RequireTenantId(), status, cancellationToken));
    }

    [HttpPost("alerts/{id:guid}/acknowledge")]
    [Authorize(Policy = "Operator")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken)
    {
        var acknowledged = await _dashboard.AcknowledgeAlertAsync(RequireActor(), id, cancellationToken);
        return acknowledged ? NoContent() : NotFound();
    }

    [HttpGet("alert-rules")]
    public async Task<ActionResult<IReadOnlyList<AlertRuleDto>>> AlertRules(CancellationToken cancellationToken)
    {
        return Ok(await _dashboard.ListAlertRulesAsync(RequireTenantId(), cancellationToken));
    }

    [HttpPost("alert-rules")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<AlertRuleDto>> CreateAlertRule(
        CreateAlertRuleRequest request,
        CancellationToken cancellationToken)
    {
        var rule = await _dashboard.CreateAlertRuleAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/alert-rules/{rule.Id}", rule);
    }

    [HttpGet("audit")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<IReadOnlyList<AuditEventDto>>> Audit(
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _dashboard.ListAuditAsync(RequireTenantId(), take, cancellationToken));
    }

    [HttpGet("usage")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<UsageSummaryDto>> Usage(CancellationToken cancellationToken)
    {
        return Ok(await _dashboard.GetUsageAsync(RequireTenantId(), cancellationToken));
    }
}
