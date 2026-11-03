using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Domain;

namespace Support360.Api.Controllers;

[ApiController]
public abstract class TenantControllerBase : ControllerBase
{
    protected RequestActor RequireActor()
    {
        var tenantClaim = User.FindFirst("tenant_id")?.Value;
        if (!Guid.TryParse(tenantClaim, out var tenantId))
        {
            throw new ForbiddenException("The authenticated principal has no tenant scope.");
        }

        var userIdValue = User.FindFirst(JwtRegisteredClaimNamesSub)?.Value
                          ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        Guid? userId = Guid.TryParse(userIdValue, out var parsedId) ? parsedId : null;
        var displayName = User.FindFirst("name")?.Value
                          ?? User.Identity?.Name
                          ?? "unknown";
        var roleValue = User.FindFirst("role")?.Value
                        ?? User.FindFirst(ClaimTypes.Role)?.Value
                        ?? nameof(UserRole.Viewer);
        if (!Enum.TryParse<UserRole>(roleValue, true, out var role))
        {
            throw new ForbiddenException("The authenticated principal has no valid role.");
        }

        return new RequestActor(
            userId,
            tenantId,
            displayName,
            role,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            userIdValue ?? string.Empty);
    }

    protected Guid RequireTenantId() => RequireActor().TenantId;

    private const string JwtRegisteredClaimNamesSub = "sub";
}
