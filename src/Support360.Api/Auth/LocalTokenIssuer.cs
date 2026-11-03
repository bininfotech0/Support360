using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Support360.Application.Abstractions;
using Support360.Application.Options;
using Support360.Domain.Entities;

namespace Support360.Api.Auth;

public sealed class LocalTokenIssuer : IUserTokenIssuer
{
    private readonly LocalAuthOptions _options;
    private readonly IClock _clock;

    public LocalTokenIssuer(IOptions<LocalAuthOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public (string Token, DateTimeOffset ExpiresAt) Issue(User user, Tenant tenant)
    {
        if (!_options.LocalEnabled)
        {
            throw new InvalidOperationException("Local token issuing is disabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must be at least 32 characters for local authentication.");
        }

        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_options.TokenLifetimeMinutes <= 0 ? 60 : _options.TokenLifetimeMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new("tenant_name", tenant.Name),
            new("name", user.DisplayName),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("role", user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
