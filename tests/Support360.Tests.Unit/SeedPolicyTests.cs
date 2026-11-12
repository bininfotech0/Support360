using Support360.Application.Options;
using Support360.Application.Security;

namespace Support360.Tests.Unit;

public sealed class SeedPolicyTests
{
    [Fact]
    public void DisabledSeedIsAllowedInAnyEnvironment()
    {
        SeedPolicy.EnsureAllowed(new SeedOptions { Enabled = false, AdminPassword = "" }, isDevelopment: false);
    }

    [Fact]
    public void EnabledSeedIsRejectedOutsideDevelopmentByDefault()
    {
        var options = new SeedOptions { Enabled = true, AdminPassword = "A-Strong-Demo-Password-123!" };

        var error = Assert.Throws<InvalidOperationException>(() =>
            SeedPolicy.EnsureAllowed(options, isDevelopment: false));

        Assert.Contains("Seed:AllowInProduction", error.Message);
    }

    [Fact]
    public void EnabledSeedMayRunOutsideDevelopmentWithExplicitOptIn()
    {
        var options = new SeedOptions
        {
            Enabled = true,
            AllowInProduction = true,
            AdminPassword = "A-Strong-Demo-Password-123!"
        };

        SeedPolicy.EnsureAllowed(options, isDevelopment: false);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    public void EnabledSeedRequiresAStrongAdminPassword(string password)
    {
        var options = new SeedOptions { Enabled = true, AdminPassword = password };

        var error = Assert.Throws<InvalidOperationException>(() =>
            SeedPolicy.EnsureAllowed(options, isDevelopment: true));

        Assert.Contains("Seed:AdminPassword", error.Message);
    }

    [Fact]
    public void EnabledSeedAcceptsAValidDevelopmentPassword()
    {
        var options = new SeedOptions { Enabled = true, AdminPassword = "DevOnly-ChangeMe-123!" };

        SeedPolicy.EnsureAllowed(options, isDevelopment: true);
    }
}
