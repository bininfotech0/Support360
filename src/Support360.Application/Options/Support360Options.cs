namespace Support360.Application.Options;

public sealed class AgentPlatformOptions
{
    public const string SectionName = "AgentPlatform";

    public int HeartbeatIntervalSeconds { get; set; } = 30;
    public int TelemetryIntervalSeconds { get; set; } = 60;
    public int OfflineAfterSeconds { get; set; } = 90;
    public int MaxMetricsPerBatch { get; set; } = 500;
    public int MaxJobEventsPerBatch { get; set; } = 200;
    public int MaxMetricAgeHours { get; set; } = 24;
    public int MaxClockSkewMinutes { get; set; } = 5;
    public int EnrollmentLifetimeMinutes { get; set; } = 15;
    public int AgentCredentialLifetimeDays { get; set; } = 0;
}

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public int TelemetryDays { get; set; } = 7;
    public int AuditDays { get; set; } = 365;
    public int ResolvedAlertDays { get; set; } = 90;
    public int ApplicationCheckResultDays { get; set; } = 30;
    public int UsageDays { get; set; } = 400;
    public int RunIntervalMinutes { get; set; } = 60;
}

public sealed class DeploymentOptions
{
    public const string SectionName = "Deployment";

    public bool ExecutionEnabled { get; set; }
    public string? ManifestSigningPublicKey { get; set; }
    public int ManifestLifetimeMinutes { get; set; } = 30;
}

public sealed class LocalAuthOptions
{
    public const string SectionName = "Auth";

    public string Mode { get; set; } = "Oidc";
    public bool LocalEnabled { get; set; }
    public string Issuer { get; set; } = "support360-local";
    public string Audience { get; set; } = "support360-api";
    public string? SigningKey { get; set; }
    public int TokenLifetimeMinutes { get; set; } = 60;
    public string? OidcAuthority { get; set; }
    public string? OidcAudience { get; set; }
    public string TenantClaim { get; set; } = "tenant_id";
    public List<string> OidcRoleClaims { get; set; } = ["role", "roles"];
    public Dictionary<string, string> OidcRoleMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string OidcNameClaim { get; set; } = "name";
    public string OidcEmailClaim { get; set; } = "email";
    public string OidcSubjectClaim { get; set; } = "sub";
    public bool OidcRequireRoleClaim { get; set; } = true;
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; }
    public string TenantName { get; set; } = "Demo Operations";
    public string TenantSlug { get; set; } = "demo";
    public string AdminEmail { get; set; } = "admin@support360.local";
    public string AdminDisplayName { get; set; } = "Support360 Administrator";
    public string AdminPassword { get; set; } = string.Empty;
    public bool IsDemoTenant { get; set; } = true;
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    public int EnrollmentPermitLimit { get; set; } = 10;
    public int EnrollmentWindowMinutes { get; set; } = 5;
    public int LoginPermitLimit { get; set; } = 10;
    public int LoginWindowMinutes { get; set; } = 1;
    public int AgentPermitLimit { get; set; } = 240;
    public int AgentWindowMinutes { get; set; } = 1;
    public int UserPermitLimit { get; set; } = 600;
    public int UserWindowMinutes { get; set; } = 1;
}
