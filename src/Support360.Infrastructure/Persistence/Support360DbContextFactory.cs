using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Support360.Infrastructure.Persistence;

public sealed class Support360DbContextFactory : IDesignTimeDbContextFactory<Support360DbContext>
{
    public Support360DbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPPORT360_DB")
            ?? "Host=localhost;Port=5432;Database=support360;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<Support360DbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new Support360DbContext(options, new TenantContext());
    }
}
