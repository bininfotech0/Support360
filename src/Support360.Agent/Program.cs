using System.Runtime.InteropServices;

namespace Support360.Agent;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await ExecuteAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AgentLog.Error("agent stopped", ("error", ex.Message));
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0)
        {
            throw new InvalidOperationException("usage: support360-agent <enroll|run> [options]");
        }

        switch (args[0])
        {
            case "enroll":
                await EnrollAsync(args[1..]);
                return 0;
            case "run":
                return await RunAsync(args[1..]);
            case "service":
                var serviceFlags = Flags.Parse(args[1..], []);
                var configPath = serviceFlags.GetValueOrDefault("config");
                if (string.IsNullOrEmpty(configPath))
                {
                    throw new InvalidOperationException("-config is required");
                }
                return await WindowsServiceHost.RunServiceAsync(configPath, CancellationToken.None);
            default:
                throw new InvalidOperationException($"unknown command \"{args[0]}\"; expected enroll or run");
        }
    }

    private static async Task EnrollAsync(string[] args)
    {
        var flags = Flags.Parse(args, ["allow-http", "enable-deployments"]);
        var server = StringValue(flags, "server", Environment.GetEnvironmentVariable("SUPPORT360_SERVER_URL"));
        var token = StringValue(flags, "token", null)
            ?? Environment.GetEnvironmentVariable("SUPPORT360_ENROLLMENT_TOKEN")
            ?? throw new InvalidOperationException("-token or SUPPORT360_ENROLLMENT_TOKEN is required");
        var identityPath = StringValue(flags, "identity", AgentPaths.DefaultIdentityPath())!;
        var allowHttp = BoolValue(flags, "allow-http");
        var deploymentOptIn = BoolValue(flags, "enable-deployments");

        using var client = AgentClient.Create(server, allowHttp);
        var response = await client.EnrollAsync(token, identityPath, deploymentOptIn, CancellationToken.None);
        Console.WriteLine(
            $"Enrolled agent {response.AgentId}. Identity saved to {identityPath}. " +
            $"Heartbeat interval {response.HeartbeatIntervalSeconds}s, telemetry interval {response.TelemetryIntervalSeconds}s.");
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var flags = Flags.Parse(args, ["allow-http"]);
        var server = StringValue(flags, "server", Environment.GetEnvironmentVariable("SUPPORT360_SERVER_URL"));
        var identityPath = StringValue(flags, "identity", AgentPaths.DefaultIdentityPath())!;
        var queuePath = StringValue(flags, "queue", AgentPaths.DefaultQueuePath())!;
        var allowHttp = BoolValue(flags, "allow-http");
        var manifestPublicKey = StringValue(flags, "manifest-public-key", Environment.GetEnvironmentVariable("SUPPORT360_MANIFEST_PUBLIC_KEY"));
        var deployRoot = StringValue(flags, "deploy-root", Environment.GetEnvironmentVariable("SUPPORT360_DEPLOY_ROOT"));

        AgentIdentityValue identity;
        try
        {
            identity = AgentIdentity.Read(identityPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("load identity: " + ex.Message, ex);
        }

        using var client = AgentClient.Create(server, allowHttp);

        using var cancellation = new CancellationTokenSource();
        void Cancel(object? sender, ConsoleCancelEventArgs? eventArgs = null)
        {
            if (eventArgs is not null)
            {
                eventArgs.Cancel = true;
            }
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }
        Console.CancelKeyPress += Cancel;
        PosixSignalRegistration? sigterm = null;
        try
        {
            sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => Cancel(null));
            var runner = new AgentRunner(client, identity, queuePath,
                new DeploymentRuntimeConfig(manifestPublicKey, deployRoot));
            await runner.RunAsync(cancellation.Token);
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
            sigterm?.Dispose();
        }
    }

    private static string? StringValue(Dictionary<string, string> flags, string name, string? fallback) =>
        flags.TryGetValue(name, out var value) ? value : fallback;

    private static bool BoolValue(Dictionary<string, string> flags, string name) =>
        flags.TryGetValue(name, out var value) && value is "true" or "1";
}

internal sealed class FlagParseException : Exception
{
    public FlagParseException(string message) : base(message)
    {
    }
}

internal static class Flags
{
    public static Dictionary<string, string> Parse(string[] args, string[] booleanFlags)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith('-') || argument.Length < 2)
            {
                throw new FlagParseException($"unexpected argument: {argument}");
            }

            var body = argument.TrimStart('-');
            var separator = body.IndexOf('=');
            var name = separator < 0 ? body : body[..separator];
            var inline = separator < 0 ? null : body[(separator + 1)..];
            if (name.Length == 0)
            {
                throw new FlagParseException($"invalid flag: {argument}");
            }

            string value;
            if (booleanFlags.Contains(name))
            {
                value = inline ?? "true";
                if (value is not ("true" or "false" or "1" or "0"))
                {
                    throw new FlagParseException($"invalid boolean value for -{name}: {value}");
                }
            }
            else if (inline is not null)
            {
                value = inline;
            }
            else if (index + 1 < args.Length)
            {
                value = args[++index];
            }
            else
            {
                throw new FlagParseException($"flag needs an argument: -{name}");
            }
            values[name] = value;
        }
        return values;
    }
}
