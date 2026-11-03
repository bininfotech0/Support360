using System.Security.Claims;
using Support360.Application.Options;
using Support360.Domain;

namespace Support360.Application.Security;

public sealed record OidcClaimMappingResult(bool Succeeded, string? FailureReason)
{
    public static OidcClaimMappingResult Success { get; } = new(true, null);

    public static OidcClaimMappingResult Failure(string reason) => new(false, reason);
}

public static class OidcClaimMapper
{
    public static OidcClaimMappingResult Apply(
        ClaimsIdentity identity,
        LocalAuthOptions options,
        bool requireRole)
    {
        MapTenant(identity, options);
        var roles = MapRoles(identity, options);
        if (requireRole && roles.Count == 0)
        {
            return OidcClaimMappingResult.Failure(
                "The authenticated principal has no role claim that maps to a Support360 role.");
        }

        MapIdentityClaim(identity, "name", options.OidcNameClaim, "preferred_username");
        MapIdentityClaim(identity, "email", options.OidcEmailClaim, "preferred_username");
        MapIdentityClaim(identity, "sub", options.OidcSubjectClaim, "nameidentifier");
        return OidcClaimMappingResult.Success;
    }

    private static void MapTenant(ClaimsIdentity identity, LocalAuthOptions options)
    {
        if (identity.HasClaim(c => c.Type == "tenant_id"))
        {
            return;
        }

        var tenant = FirstNonEmpty(identity, options.TenantClaim, "tenant", "tenantId");
        if (tenant is not null)
        {
            identity.AddClaim(new Claim("tenant_id", tenant));
        }
    }

    private static HashSet<string> MapRoles(ClaimsIdentity identity, LocalAuthOptions options)
    {
        var candidates = new List<string>();
        foreach (var claimType in (options.OidcRoleClaims ?? []).DefaultIfEmpty("role").Distinct(StringComparer.Ordinal))
        {
            candidates.AddRange(identity.FindAll(claimType).SelectMany(claim => SplitRoleValues(claim.Value)));
        }

        candidates.AddRange(identity.FindAll("role").Select(claim => claim.Value));
        candidates.AddRange(identity.FindAll(ClaimTypes.Role).Select(claim => claim.Value));

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var mapped = MapRole(candidate, options);
            if (mapped is not null)
            {
                roles.Add(mapped);
            }
        }

        foreach (var existing in identity.FindAll("role").ToArray())
        {
            identity.RemoveClaim(existing);
        }

        foreach (var role in roles.OrderByDescending(role => Enum.Parse<UserRole>(role)))
        {
            identity.AddClaim(new Claim("role", role));
        }

        return roles;
    }

    private static string? MapRole(string value, LocalAuthOptions options)
    {
        if (Enum.TryParse<UserRole>(value, true, out var direct) && Enum.IsDefined(direct))
        {
            return direct.ToString();
        }

        foreach (var mapping in options.OidcRoleMappings ?? new Dictionary<string, string>())
        {
            if (string.Equals(mapping.Key, value, StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse<UserRole>(mapping.Value, true, out var mapped)
                && Enum.IsDefined(mapped))
            {
                return mapped.ToString();
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitRoleValues(string value) =>
        value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void MapIdentityClaim(ClaimsIdentity identity, string canonicalType, params string[] sourceTypes)
    {
        if (identity.HasClaim(c => c.Type == canonicalType))
        {
            return;
        }

        var value = FirstNonEmpty(identity, sourceTypes);
        if (value is null)
        {
            return;
        }

        if (canonicalType == "email" && !value.Contains('@', StringComparison.Ordinal))
        {
            return;
        }

        identity.AddClaim(new Claim(canonicalType, value));
    }

    private static string? FirstNonEmpty(ClaimsIdentity identity, params string[] claimTypes) =>
        claimTypes
            .SelectMany(type => identity.FindAll(type))
            .Select(claim => claim.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
