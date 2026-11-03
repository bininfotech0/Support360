using System.Security.Claims;
using Support360.Application.Options;
using Support360.Application.Security;

namespace Support360.Tests.Unit;

public sealed class OidcClaimMapperTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void MapsConfiguredClaimsAndRoleAliases()
    {
        var options = new LocalAuthOptions
        {
            TenantClaim = "tenant",
            OidcRoleClaims = ["roles"],
            OidcRoleMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["support360-admin"] = "TenantAdmin",
                ["support360-operator"] = "Operator"
            },
            OidcNameClaim = "display_name",
            OidcEmailClaim = "mail",
            OidcSubjectClaim = "subject"
        };
        var identity = new ClaimsIdentity([
            new Claim("subject", "external-user-1"),
            new Claim("tenant", TenantId.ToString()),
            new Claim("roles", "support360-admin"),
            new Claim("roles", "support360-operator"),
            new Claim("display_name", "Ada Lovelace"),
            new Claim("mail", "ada@example.com")
        ], "Bearer");

        var result = OidcClaimMapper.Apply(identity, options, requireRole: true);

        Assert.True(result.Succeeded);
        Assert.Equal(TenantId.ToString(), identity.FindFirst("tenant_id")?.Value);
        Assert.Equal(["TenantAdmin", "Operator"], identity.FindAll("role").Select(c => c.Value).ToArray());
        Assert.Equal("Ada Lovelace", identity.FindFirst("name")?.Value);
        Assert.Equal("ada@example.com", identity.FindFirst("email")?.Value);
        Assert.Equal("external-user-1", identity.FindFirst("sub")?.Value);
    }

    [Theory]
    [InlineData("superuser")]
    [InlineData("")]
    public void FailsClosedWhenOidcRoleDoesNotMap(string role)
    {
        var options = new LocalAuthOptions { OidcRoleClaims = ["roles"] };
        var identity = new ClaimsIdentity([
            new Claim("tenant_id", TenantId.ToString()),
            new Claim("roles", role)
        ], "Bearer");

        var result = OidcClaimMapper.Apply(identity, options, requireRole: true);

        Assert.False(result.Succeeded);
        Assert.Contains("role", result.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(identity.FindAll("role"));
    }

    [Theory]
    [InlineData("operator, Viewer", new[] { "Operator", "Viewer" })]
    [InlineData("TenantAdmin Viewer", new[] { "TenantAdmin", "Viewer" })]
    [InlineData("PlatformAdmin", new[] { "PlatformAdmin" })]
    public void NormalizesAndSplitsDirectRoleClaims(string roleValue, string[] expected)
    {
        var options = new LocalAuthOptions();
        var identity = new ClaimsIdentity([
            new Claim("tenant_id", TenantId.ToString()),
            new Claim("role", roleValue)
        ], "Bearer");

        var result = OidcClaimMapper.Apply(identity, options, requireRole: true);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, identity.FindAll("role").Select(c => c.Value).ToArray());
    }

    [Fact]
    public void KeepsExistingLocalClaimsWithoutDuplicatingRoles()
    {
        var options = new LocalAuthOptions();
        var identity = new ClaimsIdentity([
            new Claim("tenant_id", TenantId.ToString()),
            new Claim("role", "TenantAdmin"),
            new Claim("name", "Admin"),
            new Claim("email", "admin@example.com"),
            new Claim("sub", "22222222-2222-2222-2222-222222222222")
        ], "Bearer");

        var result = OidcClaimMapper.Apply(identity, options, requireRole: false);

        Assert.True(result.Succeeded);
        Assert.Single(identity.FindAll("role"));
        Assert.Equal("TenantAdmin", identity.FindFirst("role")?.Value);
        Assert.Equal("Admin", identity.FindFirst("name")?.Value);
    }

    [Fact]
    public void AllowsMissingRoleOnlyWhenLocalAuthenticationIsRequired()
    {
        var options = new LocalAuthOptions();
        var identity = new ClaimsIdentity([new Claim("tenant_id", TenantId.ToString())], "Bearer");

        Assert.False(OidcClaimMapper.Apply(identity, options, requireRole: true).Succeeded);
        Assert.True(OidcClaimMapper.Apply(identity, options, requireRole: false).Succeeded);
    }
}
