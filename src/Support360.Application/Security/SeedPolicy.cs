using Support360.Application.Options;

namespace Support360.Application.Security;

/// <summary>
/// Guards the demo data seeder so development credentials cannot silently be
/// created in a non-development environment.
/// </summary>
public static class SeedPolicy
{
    public const int MinimumAdminPasswordLength = 12;

    public static void EnsureAllowed(SeedOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return;
        }

        if (!isDevelopment && !options.AllowInProduction)
        {
            throw new InvalidOperationException(
                "Seed:Enabled must be false outside Development. Set Seed:AllowInProduction=true only for a deliberate demo deployment.");
        }

        EnsureAdminPassword(options.AdminPassword);
    }

    public static void EnsureAdminPassword(string? adminPassword)
    {
        if (string.IsNullOrWhiteSpace(adminPassword)
            || adminPassword.Length < MinimumAdminPasswordLength)
        {
            throw new InvalidOperationException(
                $"Seed:AdminPassword must contain at least {MinimumAdminPasswordLength} characters when Seed:Enabled is true.");
        }
    }
}
