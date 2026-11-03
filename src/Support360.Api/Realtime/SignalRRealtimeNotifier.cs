using Microsoft.AspNetCore.SignalR;
using Support360.Application.Abstractions;

namespace Support360.Api.Realtime;

public sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<DashboardHub> _hub;

    public SignalRRealtimeNotifier(IHubContext<DashboardHub> hub)
    {
        _hub = hub;
    }

    public Task AgentChangedAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group(DashboardHub.TenantGroup(tenantId.ToString()))
            .SendAsync("agent.changed", new { agentId }, cancellationToken);

    public Task AgentsChangedAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group(DashboardHub.TenantGroup(tenantId.ToString()))
            .SendAsync("agents.changed", new { tenantId }, cancellationToken);

    public Task TelemetryReceivedAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group(DashboardHub.TenantGroup(tenantId.ToString()))
            .SendAsync("telemetry.received", new { agentId }, cancellationToken);

    public Task AlertsChangedAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group(DashboardHub.TenantGroup(tenantId.ToString()))
            .SendAsync("alerts.changed", new { tenantId }, cancellationToken);

    public Task DeploymentChangedAsync(Guid tenantId, Guid deploymentId, CancellationToken cancellationToken = default) =>
        _hub.Clients.Group(DashboardHub.TenantGroup(tenantId.ToString()))
            .SendAsync("deployment.changed", new { deploymentId }, cancellationToken);
}
