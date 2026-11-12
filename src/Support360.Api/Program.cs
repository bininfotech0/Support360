using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Support360.Api.Auth;
using Support360.Api.Realtime;
using Support360.Application.Abstractions;
using Support360.Application.Options;
using Support360.Application.Security;
using Support360.Infrastructure;
using Support360.Infrastructure.Persistence;
using Support360.Workers;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var authOptions = configuration.GetSection(LocalAuthOptions.SectionName).Get<LocalAuthOptions>() ?? new LocalAuthOptions();
var useLocalAuth = authOptions.LocalEnabled;
if (useLocalAuth)
{
    if (!builder.Environment.IsDevelopment() && !configuration.GetValue("Auth:AllowLocalInNonDevelopment", false))
    {
        throw new InvalidOperationException(
            "Local authentication is enabled outside Development. Use OIDC or explicitly accept the risk with Auth:AllowLocalInNonDevelopment.");
    }

    if (string.IsNullOrWhiteSpace(authOptions.SigningKey) || authOptions.SigningKey.Length < 32)
    {
        throw new InvalidOperationException("Auth:SigningKey must contain at least 32 characters when local authentication is enabled.");
    }
}
else
{
    if (!string.Equals(authOptions.Mode, "Oidc", StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrWhiteSpace(authOptions.OidcAuthority)
        || string.IsNullOrWhiteSpace(authOptions.OidcAudience))
    {
        throw new InvalidOperationException(
            "Authentication is not configured. Configure OIDC (Auth:Mode=Oidc, Auth:OidcAuthority, Auth:OidcAudience) or enable local auth only for Development.");
    }
}

var seedOptions = configuration.GetSection(SeedOptions.SectionName).Get<SeedOptions>() ?? new SeedOptions();
SeedPolicy.EnsureAllowed(seedOptions, builder.Environment.IsDevelopment());

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<Support360DbContext>("database", tags: ["ready"]);

builder.Services.AddSupport360Infrastructure(configuration);
builder.Services.AddSupport360Workers(configuration);

builder.Services.AddSingleton<IUserTokenIssuer, LocalTokenIssuer>();
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = useLocalAuth,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };

        if (useLocalAuth)
        {
            options.TokenValidationParameters.ValidIssuer = authOptions.Issuer;
            options.TokenValidationParameters.ValidAudience = authOptions.Audience;
            options.TokenValidationParameters.IssuerSigningKey =
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey!));
        }
        else
        {
            options.Authority = authOptions.OidcAuthority;
            options.Audience = authOptions.OidcAudience;
            options.RequireHttpsMetadata = true;
        }

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs/dashboard"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    var mapping = OidcClaimMapper.Apply(identity, authOptions, requireRole: !useLocalAuth);
                    if (!mapping.Succeeded)
                    {
                        context.Fail(mapping.FailureReason ?? "OIDC claim mapping failed.");
                    }
                }

                return Task.CompletedTask;
            }
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(
        AgentKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("TenantUser", policy => policy.RequireAuthenticatedUser().RequireClaim("tenant_id"))
    .AddPolicy("Operator", policy => policy.RequireAuthenticatedUser().RequireRole("Operator", "TenantAdmin", "PlatformAdmin"))
    .AddPolicy("TenantAdmin", policy => policy.RequireAuthenticatedUser().RequireRole("TenantAdmin", "PlatformAdmin"))
    .AddPolicy("Approver", policy => policy.RequireAuthenticatedUser().RequireRole("Approver", "TenantAdmin", "PlatformAdmin"));

var rateOptions = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("enroll", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateOptions.EnrollmentPermitLimit,
            Window = TimeSpan.FromMinutes(rateOptions.EnrollmentWindowMinutes),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateOptions.LoginPermitLimit,
            Window = TimeSpan.FromMinutes(rateOptions.LoginWindowMinutes),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("agent", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("agent_id")?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateOptions.AgentPermitLimit,
            Window = TimeSpan.FromMinutes(rateOptions.AgentWindowMinutes),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("user", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateOptions.UserPermitLimit,
            Window = TimeSpan.FromMinutes(rateOptions.UserWindowMinutes),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("web", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
}

if (configuration.GetValue("ReverseProxy:Enabled", false))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (configuration.GetValue("ReverseProxy:Enabled", false))
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ApiExceptionMiddleware>();

if (configuration.GetValue("Http:UseHttpsRedirection", false))
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

if (allowedOrigins.Length > 0)
{
    app.UseCors("web");
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapOpenApi();
app.MapControllers();
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

await InitializeDatabaseAsync(app);

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
    var provider = app.Configuration["Database:Provider"] ?? "Postgres";

    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        if (app.Environment.IsProduction())
        {
            throw new InvalidOperationException("SQLite is not supported in Production. Configure PostgreSQL.");
        }

        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }

    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
}

public partial class Program;
