using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Application.Options;
using Support360.Contracts.Auth;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class UserAuthenticationService : IUserAuthenticationService
{
    private static readonly string DummyHash = new Support360.Infrastructure.Security.Pbkdf2PasswordHasher().Hash("support360-dummy-password");

    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUserTokenIssuer _tokenIssuer;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly LocalAuthOptions _options;

    public UserAuthenticationService(
        ISupport360DbContext db,
        ITenantContext tenantContext,
        IPasswordHasher passwordHasher,
        IUserTokenIssuer tokenIssuer,
        IAuditService audit,
        IClock clock,
        IOptions<LocalAuthOptions> options)
    {
        _db = db;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
        _audit = audit;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<(LoginResponse? Response, string? Failure)> LoginAsync(
        string email,
        string password,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        if (!_options.LocalEnabled)
        {
            return (null, "Local authentication is disabled.");
        }

        var normalized = email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .SingleOrDefaultAsync(u => u.EmailNormalized == normalized, cancellationToken);

        if (user is null)
        {
            _passwordHasher.Verify(password, DummyHash);
            return (null, "Invalid email or password.");
        }

        _tenantContext.SetTenant(user.TenantId);

        if (!user.IsActive || user.Tenant.Status != TenantStatus.Active)
        {
            return (null, "This account is not available.");
        }

        if (!_passwordHasher.Verify(password, user.PasswordHash))
        {
            await _audit.WriteAsync(
                user.TenantId,
                AuditActorType.User,
                user.Id,
                user.DisplayName,
                "auth.login.failed",
                "User",
                user.Id.ToString(),
                "failure",
                null,
                ipAddress,
                cancellationToken);
            return (null, "Invalid email or password.");
        }

        var now = _clock.UtcNow;
        user.LastLoginAt = now;
        user.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        var (token, expiresAt) = _tokenIssuer.Issue(user, user.Tenant);
        var userDto = new UserDto(
            user.Id,
            user.TenantId,
            user.Tenant.Name,
            user.Tenant.IsDemo,
            user.Email,
            user.DisplayName,
            user.Role.ToString());

        await _audit.WriteAsync(
            user.TenantId,
            AuditActorType.User,
            user.Id,
            user.DisplayName,
            "auth.login.succeeded",
            "User",
            user.Id.ToString(),
            "success",
            null,
            ipAddress,
            cancellationToken);

        return (new LoginResponse(token, expiresAt, userDto), null);
    }
}
