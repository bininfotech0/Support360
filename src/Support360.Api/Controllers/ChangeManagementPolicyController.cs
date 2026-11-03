using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Operations;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/change-management/policy")]
[Authorize(Policy = "TenantUser")]
public sealed class ChangeManagementPolicyController : TenantControllerBase
{
    private readonly IIncidentChangeService _operations;
    public ChangeManagementPolicyController(IIncidentChangeService operations) => _operations = operations;

    [HttpGet]
    public Task<ChangeApprovalPolicyDto> Get(CancellationToken cancellationToken) =>
        _operations.GetPolicyAsync(RequireTenantId(), cancellationToken);

    [HttpPut]
    [Authorize(Policy = "TenantAdmin")]
    public Task<ChangeApprovalPolicyDto> Update(UpdateChangeApprovalPolicyRequest request, CancellationToken cancellationToken) =>
        _operations.UpdatePolicyAsync(RequireActor(), request, cancellationToken);
}
