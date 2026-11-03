using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Support360.Workers.Jobs;

namespace Support360.Workers;

public static class DependencyInjection
{
    public static IServiceCollection AddSupport360Workers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var enabled = configuration.GetValue("BackgroundWorkers:Enabled", true);
        if (!enabled)
        {
            return services;
        }

        services.AddHostedService<AlertEvaluationWorker>();
        services.AddHostedService<AgentLivenessWorker>();
        services.AddHostedService<RetentionWorker>();
        return services;
    }
}
