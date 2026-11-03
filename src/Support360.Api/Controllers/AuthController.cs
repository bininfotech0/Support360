using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Support360.Application.Abstractions;
using Support360.Contracts.Auth;

namespace Support360.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : TenantControllerBase
{
    private readonly IValidator<LoginRequest> _validator;
    private readonly IUserAuthenticationService _authenticationService;
    private readonly ISupport360DbContext _db;

    public AuthController(
        IValidator<LoginRequest> validator,
        IUserAuthenticationService authenticationService,
        ISupport360DbContext db)
    {
        _validator = validator;
        _authenticationService = authenticationService;
        _db = db;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);
        var (response, failure) = await _authenticationService.LoginAsync(
            request.Email,
            request.Password,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        if (response is null)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed", detail: failure);
        }

        return Ok(response);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
    {
        var actor = RequireActor();
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.Tenant)
            .SingleOrDefaultAsync(u => u.TenantId == actor.TenantId && u.Id == actor.UserId, cancellationToken);

        if (user is not null)
        {
            return Ok(new MeResponse(new UserDto(
                user.Id,
                user.TenantId,
                user.Tenant.Name,
                user.Tenant.IsDemo,
                user.Email,
                user.DisplayName,
                user.Role.ToString())));
        }

        var tenant = await _db.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == actor.TenantId, cancellationToken)
            ?? throw new Support360.Application.Exceptions.ForbiddenException("Tenant not found.");

        return Ok(new MeResponse(new UserDto(
            actor.UserId ?? Guid.Empty,
            tenant.Id,
            tenant.Name,
            tenant.IsDemo,
            User.FindFirst("email")?.Value ?? string.Empty,
            actor.DisplayName,
            actor.Role.ToString())));
    }
}
