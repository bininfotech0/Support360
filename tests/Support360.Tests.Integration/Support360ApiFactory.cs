using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Support360.Tests.Integration;

public sealed class Support360ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@support360.local";
    public const string AdminPassword = "Integration-Test-Password-123!";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"support360-integration-{Guid.NewGuid():N}.db");
    private readonly string _databaseProvider;
    private readonly string? _connectionString;

    public Support360ApiFactory() : this("Sqlite", null)
    {
    }

    internal Support360ApiFactory(string databaseProvider, string? connectionString)
    {
        _databaseProvider = databaseProvider;
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = _databaseProvider,
            ["ConnectionStrings:Default"] = _connectionString ?? $"Data Source={_dbPath}",
            ["Auth:Mode"] = "Local",
            ["Auth:LocalEnabled"] = "true",
            ["Auth:Issuer"] = "support360-tests",
            ["Auth:Audience"] = "support360-tests",
            ["Auth:SigningKey"] = "integration-test-signing-key-not-for-production",
            ["Auth:TokenLifetimeMinutes"] = "30",
            ["Seed:Enabled"] = "true",
            ["Seed:TenantName"] = "Integration Tenant",
            ["Seed:TenantSlug"] = "integration",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminDisplayName"] = "Integration Admin",
            ["Seed:AdminPassword"] = AdminPassword,
            ["Seed:IsDemoTenant"] = "true",
            ["BackgroundWorkers:Enabled"] = "false",
            ["Http:UseHttpsRedirection"] = "false",
            ["ReverseProxy:Enabled"] = "false",
            ["RateLimits:EnrollmentPermitLimit"] = "10000",
            ["RateLimits:LoginPermitLimit"] = "10000",
            ["RateLimits:AgentPermitLimit"] = "10000",
            ["RateLimits:UserPermitLimit"] = "10000",
            ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
        };

        foreach (var setting in settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        foreach (var path in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
