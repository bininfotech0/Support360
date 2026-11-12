using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Support360.Agent;

public sealed record ServiceConfig(string Server, string Identity, string Queue, string? ManifestPublicKey, string? DeploymentRoot);

public static class WindowsServiceHost
{
    public static ServiceConfig LoadConfig(string path)
    {
        var data = File.ReadAllText(path);
        ServiceConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<ServiceConfig>(data, AgentJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("service configuration is invalid JSON", ex);
        }
        if (config is null
            || string.IsNullOrEmpty(config.Server)
            || string.IsNullOrEmpty(config.Identity)
            || string.IsNullOrEmpty(config.Queue))
        {
            throw new InvalidDataException("service configuration is incomplete");
        }
        return config;
    }

    public static async Task<int> RunServiceAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            AgentLog.Error("agent stopped", ("error", "Windows service mode is available only on Windows; use the systemd unit on Linux"));
            return 1;
        }

        var config = LoadConfig(configPath);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddWindowsService(options => options.ServiceName = "Support360Agent");
        builder.Services.AddSingleton(config);
        builder.Services.AddHostedService<AgentService>();

        using var host = builder.Build();
        try
        {
            await host.RunAsync(cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            AgentLog.Error("agent service stopped", ("error", ex.Message));
            return 1;
        }
    }

    private sealed class AgentService(ServiceConfig config) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var identity = AgentIdentity.Read(config.Identity);
            using var client = AgentClient.Create(config.Server, allowHttp: false);
            var runner = new AgentRunner(client, identity, config.Queue,
                new DeploymentRuntimeConfig(config.ManifestPublicKey, config.DeploymentRoot));
            await runner.RunAsync(stoppingToken);
        }
    }
}
