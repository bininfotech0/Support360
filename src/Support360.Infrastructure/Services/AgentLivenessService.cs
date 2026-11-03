using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Application.Options;
using Support360.Domain;

namespace Support360.Infrastructure.Services;

public sealed class AgentLivenessService : IAgentLivenessService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IRealtimeNotifier _realtime;
    private readonly AgentPlatformOptions _options;
    private readonly ILogger<AgentLivenessService> _logger;

    public AgentLivenessService(
        ISupport360DbContext db,
        ITenantContext tenantContext,
        IClock clock,
        IRealtimeNotifier realtime,
        IOptions<AgentPlatformOptions> options,
        ILogger<AgentLivenessService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _clock = clock;
        _realtime = realtime;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> MarkStaleAgentsOfflineAsync(CancellationToken cancellationToken)
    {
        _tenantContext.SetSystem();
        var now = _clock.UtcNow;
        var threshold = now.AddSeconds(-_options.OfflineAfterSeconds);

        var staleAgents = await _db.Agents
            .IgnoreQueryFilters()
            .Where(a => a.Status == AgentStatus.Active
                        && (a.LastHeartbeatAt == null || a.LastHeartbeatAt < threshold))
            .ToListAsync(cancellationToken);

        var changed = 0;
        var tenants = new HashSet<Guid>();
        foreach (var agent in staleAgents)
        {
            if (agent.Connectivity == AgentConnectivity.Offline)
            {
                continue;
            }

            agent.MarkOffline(now);
            tenants.Add(agent.TenantId);
            changed++;
        }

        if (changed > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var tenantId in tenants)
            {
                await _realtime.AgentsChangedAsync(tenantId, cancellationToken);
            }

            _logger.LogWarning("Marked {Count} agents offline after heartbeat timeout", changed);
        }

        return changed;
    }
}
