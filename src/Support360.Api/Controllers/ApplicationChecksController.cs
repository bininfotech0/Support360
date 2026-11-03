using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Applications;

namespace Support360.Api.Controllers;

[Route("api/application-checks")]
[Authorize(Policy = "TenantUser")]
public sealed class ApplicationChecksController : TenantControllerBase
{
    private readonly IApplicationCheckService _checks;

    public ApplicationChecksController(IApplicationCheckService checks) => _checks = checks;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApplicationCheckDto>>> List(CancellationToken cancellationToken) =>
        Ok(await _checks.ListAsync(RequireTenantId(), cancellationToken));

    [HttpGet("target-policy")]
    public async Task<ActionResult<HttpCheckTargetPolicyDto>> GetTargetPolicy(CancellationToken cancellationToken) =>
        Ok(await _checks.GetHttpCheckTargetPolicyAsync(RequireTenantId(), cancellationToken));

    [HttpPut("target-policy")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<HttpCheckTargetPolicyDto>> UpdateTargetPolicy(
        UpdateHttpCheckTargetPolicyRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _checks.UpdateHttpCheckTargetPolicyAsync(RequireActor(), request, cancellationToken));

    [HttpPost]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<ApplicationCheckDto>> Create(
        CreateApplicationCheckRequest request,
        CancellationToken cancellationToken)
    {
        var check = await _checks.CreateAsync(RequireActor(), request, cancellationToken);
        return CreatedAtAction(nameof(List), new { id = check.Id }, check);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<ApplicationCheckDto>> Update(
        Guid id,
        UpdateApplicationCheckRequest request,
        CancellationToken cancellationToken)
    {
        var check = await _checks.UpdateAsync(RequireActor(), id, request, cancellationToken);
        return check is null ? NotFound() : Ok(check);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await _checks.DeleteAsync(RequireActor(), id, cancellationToken) ? NoContent() : NotFound();

    [HttpGet("{id:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<ApplicationCheckHistoryItemDto>>> History(
        Guid id,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await _checks.HistoryAsync(RequireTenantId(), id, take, cancellationToken));
}
