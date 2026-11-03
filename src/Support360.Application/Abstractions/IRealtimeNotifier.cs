namespace Support360.Application.Abstractions;

public interface IRealtimeNotifier
{
    Task AgentChangedAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken = default);
    Task AgentsChangedAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task TelemetryReceivedAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken = default);
    Task AlertsChangedAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task DeploymentChangedAsync(Guid tenantId, Guid deploymentId, CancellationToken cancellationToken = default);
}
