using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Application.Options;
using Support360.Application.Security;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Persistence;

public sealed class DatabaseSeeder
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IClock _clock;
    private readonly SeedOptions _options;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        ISupport360DbContext db,
        ITenantContext tenantContext,
        IPasswordHasher passwordHasher,
        IClock clock,
        IOptions<SeedOptions> options,
        ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _passwordHasher = passwordHasher;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.AdminPassword) || _options.AdminPassword.Length < 12)
        {
            throw new InvalidOperationException(
                "Seed:AdminPassword must be configured with at least 12 characters when Seed:Enabled is true.");
        }

        _tenantContext.SetSystem();

        var email = _options.AdminEmail.Trim().ToLowerInvariant();
        var existingUser = await _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.EmailNormalized == email, cancellationToken);

        if (existingUser)
        {
            return false;
        }

        var now = _clock.UtcNow;
        var tenant = await _db.Tenants
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(t => t.Slug == _options.TenantSlug, cancellationToken);

        if (tenant is null)
        {
            tenant = new Tenant
            {
                Name = _options.TenantName,
                Slug = _options.TenantSlug,
                Status = TenantStatus.Active,
                IsDemo = _options.IsDemoTenant,
                CreatedAt = now
            };
            _db.Tenants.Add(tenant);
        }

        var user = new User
        {
            TenantId = tenant.Id,
            Email = _options.AdminEmail.Trim(),
            EmailNormalized = email,
            DisplayName = _options.AdminDisplayName,
            PasswordHash = _passwordHasher.Hash(_options.AdminPassword),
            Role = UserRole.TenantAdmin,
            IsActive = true,
            CreatedAt = now
        };
        _db.Users.Add(user);

        var hasRules = await _db.AlertRules
            .IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == tenant.Id, cancellationToken);

        if (!hasRules)
        {
            _db.AlertRules.AddRange(
                new AlertRule
                {
                    TenantId = tenant.Id,
                    Name = "CPU above 90%",
                    Metric = MetricCatalog.CpuUsagePercent,
                    Operator = ComparisonOperator.GreaterThan,
                    Threshold = 90,
                    Severity = AlertSeverity.Critical,
                    ConsecutiveBreaches = 2,
                    Enabled = true,
                    CreatedAt = now
                },
                new AlertRule
                {
                    TenantId = tenant.Id,
                    Name = "Memory above 85%",
                    Metric = MetricCatalog.MemoryUsedPercent,
                    Operator = ComparisonOperator.GreaterThan,
                    Threshold = 85,
                    Severity = AlertSeverity.Warning,
                    ConsecutiveBreaches = 2,
                    Enabled = true,
                    CreatedAt = now
                });
        }

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogWarning(
            "Seeded {Tenant} tenant and administrator {Email}. This seed is for development/demo use only.",
            tenant.Name, _options.AdminEmail);
        return true;
    }
}
