namespace Support360.Domain.Entities;

public sealed class Agent : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public required string Hostname { get; set; }
    public AgentPlatform Platform { get; set; }
    public string? OsVersion { get; set; }
    public string? Architecture { get; set; }
    public string? Version { get; set; }
    public AgentStatus Status { get; set; } = AgentStatus.Active;
    public AgentConnectivity Connectivity { get; set; } = AgentConnectivity.Unknown;
    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset? LastTelemetryAt { get; set; }
    public string? CapabilitiesJson { get; set; }
    public bool DeploymentExecutionAllowed { get; set; }
    public bool CollectorHealthy { get; set; } = true;
    public string? CollectorHealthDetail { get; set; }
    public long? UptimeSeconds { get; set; }
    public ICollection<AgentCredential> Credentials { get; set; } = [];

    public void RecordHeartbeat(
        DateTimeOffset occurredAt,
        string? version,
        string? osVersion,
        string? architecture,
        string? capabilitiesJson,
        bool collectorHealthy,
        string? collectorHealthDetail,
        long? uptimeSeconds)
    {
        LastHeartbeatAt = occurredAt;
        Connectivity = AgentConnectivity.Online;
        Version = version ?? Version;
        OsVersion = osVersion ?? OsVersion;
        Architecture = architecture ?? Architecture;
        CapabilitiesJson = capabilitiesJson ?? CapabilitiesJson;
        CollectorHealthy = collectorHealthy;
        CollectorHealthDetail = collectorHealthDetail;
        UptimeSeconds = uptimeSeconds;
        UpdatedAt = occurredAt;
    }

    public void MarkOffline(DateTimeOffset now)
    {
        if (Status == AgentStatus.Active && Connectivity != AgentConnectivity.Offline)
        {
            Connectivity = AgentConnectivity.Offline;
            UpdatedAt = now;
        }
    }

    public void Revoke(DateTimeOffset now)
    {
        Status = AgentStatus.Revoked;
        Connectivity = AgentConnectivity.Offline;
        UpdatedAt = now;
    }
}
