using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Support360.Application.Abstractions;
using Support360.Application.Options;
using Support360.Application.Validation;
using Support360.Infrastructure.Persistence;
using Support360.Infrastructure.Security;
using Support360.Infrastructure.Services;

namespace Support360.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSupport360Infrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Postgres";
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");

        services.AddDbContext<Support360DbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.EnableRetryOnFailure(3);
                    npgsql.CommandTimeout(30);
                });
            }
        });

        services.AddScoped<ISupport360DbContext>(sp => sp.GetRequiredService<Support360DbContext>());
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<DatabaseSeeder>();

        services.Configure<AgentPlatformOptions>(configuration.GetSection(AgentPlatformOptions.SectionName));
        services.Configure<RetentionOptions>(configuration.GetSection(RetentionOptions.SectionName));
        services.Configure<DeploymentOptions>(configuration.GetSection(DeploymentOptions.SectionName));
        services.Configure<LocalAuthOptions>(configuration.GetSection(LocalAuthOptions.SectionName));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));

        services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISecretGenerator, CryptographicSecretGenerator>();
        services.AddSingleton<ISecretHasher, Sha256SecretHasher>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IEnrollmentService, EnrollmentService>();
        services.AddScoped<IAgentService, AgentService>();
        services.AddScoped<ITelemetryIngestionService, TelemetryIngestionService>();
        services.AddScoped<IApplicationCheckService, ApplicationCheckService>();
        services.AddScoped<IBatchJobService, BatchJobService>();
        services.AddScoped<IDeploymentPackageService, DeploymentPackageService>();
        services.AddScoped<IDeploymentService, DeploymentService>();
        services.AddScoped<IIncidentChangeService, IncidentChangeService>();
        services.AddScoped<IServiceRequestService, ServiceRequestService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAlertEvaluationService, AlertEvaluationService>();
        services.AddScoped<IAgentLivenessService, AgentLivenessService>();
        services.AddScoped<IRetentionService, RetentionService>();
        services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();

        return services;
    }
}
