using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Domain;

namespace Support360.Api.Auth;

public sealed class AgentKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AgentKey";

    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretHasher _secretHasher;
    private readonly IClock _clock;

    public AgentKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISupport360DbContext db,
        ITenantContext tenantContext,
        ISecretHasher secretHasher,
        IClock clock)
        : base(options, logger, encoder)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretHasher = secretHasher;
        _clock = clock;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values))
        {
            return AuthenticateResult.NoResult();
        }

        var header = values.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = header["Bearer ".Length..].Trim();
        if (apiKey.Length is < 20 or > 256)
        {
            return AuthenticateResult.Fail("Invalid agent credential.");
        }

        var hash = _secretHasher.Hash(apiKey);
        var now = _clock.UtcNow;

        var credential = await _db.AgentCredentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(c => c.Agent)
            .ThenInclude(a => a.Tenant)
            .SingleOrDefaultAsync(c => c.KeyHash == hash);

        if (credential is null
            || !credential.IsUsable(now)
            || credential.Agent.Status != AgentStatus.Active
            || credential.Agent.Tenant.Status != TenantStatus.Active)
        {
            return AuthenticateResult.Fail("Invalid agent credential.");
        }

        _tenantContext.SetTenant(credential.TenantId);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, credential.AgentId.ToString()),
            new("agent_id", credential.AgentId.ToString()),
            new("tenant_id", credential.TenantId.ToString()),
            new(ClaimTypes.Role, "Agent"),
            new("name", credential.Agent.Hostname)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
