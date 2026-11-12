namespace Support360.Agent;

public sealed class AgentRunner
{
    private const int TelemetryQueueMaxEvents = 5000;
    private const int TelemetryQueueMaxBatch = 200;
    private const int ResultQueueMaxEvents = 500;
    private const int ResultQueueMaxBatch = 100;
    private const int MinimumTelemetryIntervalSeconds = 10;
    private const int MaximumTelemetryIntervalSeconds = 3600;
    private const int MinimumCheckIntervalSeconds = 30;
    private static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromSeconds(MinimumCheckIntervalSeconds);
    private static readonly TimeSpan DeploymentPollInterval = TimeSpan.FromMinutes(1);

    private static readonly string[] DefaultMetrics =
    [
        "cpu.usage.percent",
        "memory.used.percent",
        "memory.used.bytes",
        "disk.used.percent",
        "disk.free.bytes",
        "network.rx.bytes_total",
        "network.tx.bytes_total",
        "uptime.seconds"
    ];

    private readonly AgentClient _client;
    private readonly AgentIdentityValue _identity;
    private readonly string _queuePath;
    private readonly HostCollector _collector;
    private readonly DeploymentRuntimeConfig _deployment;
    private readonly string _apiKey;

    private PersistentQueue<MetricSample> _telemetry = null!;
    private PersistentQueue<ApplicationCheckResult> _results = null!;
    private List<ApplicationCheck> _checks = [];
    private readonly Dictionary<string, DateTimeOffset> _lastChecks = [];
    private HashSet<string> _allowedMetrics = [.. DefaultMetrics];
    private bool _collectorHealthy;
    private string _collectorHealth = "collection has not completed yet";

    public AgentRunner(AgentClient client, AgentIdentityValue identity, string queuePath,
        DeploymentRuntimeConfig deployment, HostCollector? collector = null)
    {
        _client = client;
        _identity = identity;
        _queuePath = queuePath;
        _deployment = deployment;
        _collector = collector ?? new HostCollector();
        _apiKey = identity.ApiKey;
    }

    public static DeploymentRuntimeConfig DeploymentConfigFromEnvironment() => new(
        Environment.GetEnvironmentVariable("SUPPORT360_MANIFEST_PUBLIC_KEY"),
        Environment.GetEnvironmentVariable("SUPPORT360_DEPLOY_ROOT"));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_identity.ApiKey) || string.IsNullOrEmpty(_identity.AgentId))
        {
            throw new InvalidOperationException("agent identity is incomplete");
        }

        _telemetry = PersistentQueue<MetricSample>.Open(_queuePath, TelemetryQueueMaxEvents, TelemetryQueueMaxBatch,
            "telemetry queue file is invalid");
        _results = PersistentQueue<ApplicationCheckResult>.Open(_queuePath + ".checks", ResultQueueMaxEvents, ResultQueueMaxBatch,
            "application result queue file is invalid");

        await LoadChecksAsync(cancellationToken);
        await LoadConfigAsync(cancellationToken);
        var telemetryInterval = TimeSpan.FromSeconds(_telemetryIntervalSeconds);
        var heartbeatInterval = TimeSpan.FromSeconds(_heartbeatIntervalSeconds);

        await CollectAsync(cancellationToken);
        await HeartbeatAsync(cancellationToken);
        await RunChecksAsync(cancellationToken);

        var nextTelemetry = DateTimeOffset.UtcNow.Add(telemetryInterval);
        var nextHeartbeat = DateTimeOffset.UtcNow.Add(heartbeatInterval);
        var nextDeploymentPoll = DateTimeOffset.UtcNow.Add(DeploymentPollInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var next = Min(nextTelemetry, nextHeartbeat, nextDeploymentPoll);
            if (next > now)
            {
                try
                {
                    await Task.Delay(next - now, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            now = DateTimeOffset.UtcNow;
            if (now >= nextTelemetry)
            {
                nextTelemetry = now.Add(telemetryInterval);
                await CollectAsync(cancellationToken);
                await RefreshChecksAsync(cancellationToken);
                await RunChecksAsync(cancellationToken);
            }
            if (now >= nextHeartbeat)
            {
                nextHeartbeat = now.Add(heartbeatInterval);
                await HeartbeatAsync(cancellationToken);
            }
            if (now >= nextDeploymentPoll)
            {
                nextDeploymentPoll = now.Add(DeploymentPollInterval);
                await PollDeploymentsAsync(cancellationToken);
            }
        }
    }

    private int _telemetryIntervalSeconds = 60;
    private int _heartbeatIntervalSeconds = 30;

    private async Task LoadChecksAsync(CancellationToken cancellationToken)
    {
        try
        {
            var checks = await _client.GetApplicationChecksAsync(_apiKey, cancellationToken);
            if (checks is not null)
            {
                _checks = [.. checks];
            }
        }
        catch (Exception ex)
        {
            AgentLog.Warn("application check configuration unavailable", ("error", ex.Message));
        }
    }

    private async Task RefreshChecksAsync(CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _client.GetApplicationChecksAsync(_apiKey, cancellationToken);
            if (updated is not null)
            {
                _checks = [.. updated];
            }
        }
        catch (Exception ex)
        {
            ThrowIfUnauthorized(ex);
        }
    }

    private async Task LoadConfigAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = await _client.GetConfigAsync(_apiKey, cancellationToken);
            if (config is null)
            {
                throw new InvalidOperationException("server returned an empty agent configuration");
            }
            _telemetryIntervalSeconds = ClampInterval(config.TelemetryIntervalSeconds, 60);
            _heartbeatIntervalSeconds = ClampInterval(config.HeartbeatIntervalSeconds, 30);
            if (config.AllowedMetrics is { Count: > 0 })
            {
                _allowedMetrics = [.. config.AllowedMetrics];
            }
        }
        catch (Exception ex)
        {
            AgentLog.Warn("agent configuration unavailable; using conservative local interval", ("error", ex.Message));
        }
    }

    private static int ClampInterval(int value, int fallback) =>
        value < MinimumTelemetryIntervalSeconds || value > MaximumTelemetryIntervalSeconds ? fallback : value;

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        List<MetricSample> samples;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            samples = [.. await _collector.CollectAsync(timeout.Token)];
        }
        catch (Exception ex)
        {
            _collectorHealthy = false;
            _collectorHealth = Truncate(ex.Message, 1000);
            AgentLog.Warn("metric collection failed", ("error", ex.Message));
            return;
        }
        _collectorHealthy = true;
        _collectorHealth = "ok";

        var filtered = samples.Where(sample => _allowedMetrics.Contains(sample.Metric)).ToList();
        try
        {
            _telemetry.Add(filtered);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("persist telemetry queue: " + ex.Message, ex);
        }

        var batch = _telemetry.Batch();
        if (batch.Count == 0)
        {
            return;
        }

        TelemetryResponse? response = null;
        var uploaded = false;
        try
        {
            response = await _client.SendTelemetryAsync(_apiKey, batch, cancellationToken);
            uploaded = response is not null;
        }
        catch (Exception ex)
        {
            ThrowIfUnauthorized(ex);
            AgentLog.Warn("telemetry upload failed; retaining queued events", ("queueDepth", _telemetry.Len), ("error", ex.Message));
        }

        if (uploaded)
        {
            var ack = Math.Min(response!.Accepted + response.Duplicates + response.Rejected, batch.Count);
            try
            {
                _telemetry.Acknowledge(ack);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("acknowledge telemetry queue: " + ex.Message, ex);
            }
            if (response.Rejected > 0)
            {
                AgentLog.Warn("server rejected telemetry samples", ("rejected", response.Rejected));
            }
        }

        if (_telemetry.Len > 0)
        {
            AgentLog.Info("agent queue status", ("queuedEvents", _telemetry.Len));
        }
    }

    private async Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        HostDetails details;
        try
        {
            details = HostCollector.CaptureHostDetails();
        }
        catch (Exception ex)
        {
            AgentLog.Warn("heartbeat upload failed", ("error", ex.Message));
            return;
        }

        try
        {
            await _client.HeartbeatAsync(_apiKey, new HeartbeatRequest(
                AgentClient.Version, details.OsVersion, details.Architecture, details.UptimeSeconds,
                _collectorHealthy, _collectorHealth,
                [.. AgentPlatformInfo.Capabilities(_identity.DeploymentOptIn)], []), cancellationToken);
        }
        catch (Exception ex)
        {
            ThrowIfUnauthorized(ex);
            AgentLog.Warn("heartbeat upload failed", ("error", ex.Message));
        }
    }

    private async Task RunChecksAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var batch = new List<ApplicationCheckResult>();
        foreach (var check in _checks)
        {
            var interval = TimeSpan.FromSeconds(check.IntervalSeconds);
            if (interval < MinimumCheckInterval)
            {
                interval = MinimumCheckInterval;
            }
            if (_lastChecks.TryGetValue(check.Id, out var last) && now - last < interval)
            {
                continue;
            }
            _lastChecks[check.Id] = now;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            batch.Add(await ApplicationChecks.ExecuteAsync(check, timeout.Token));
        }

        try
        {
            _results.Add(batch);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("persist application result queue: " + ex.Message, ex);
        }

        var pending = _results.Batch();
        if (pending.Count == 0)
        {
            return;
        }

        ApplicationCheckResultsResponse? response;
        try
        {
            response = await _client.SendApplicationCheckResultsAsync(_apiKey, pending, cancellationToken);
        }
        catch (Exception ex)
        {
            ThrowIfUnauthorized(ex);
            AgentLog.Warn("application check results upload failed", ("error", ex.Message));
            return;
        }
        if (response is null)
        {
            AgentLog.Warn("application check results upload failed", ("error", "server returned an empty response"));
            return;
        }

        var ack = Math.Min(response.Accepted + response.Duplicates + response.Rejected, pending.Count);
        try
        {
            _results.Acknowledge(ack);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("acknowledge application result queue: " + ex.Message, ex);
        }
        if (response.Rejected > 0)
        {
            AgentLog.Warn("server rejected application check results", ("rejected", response.Rejected));
        }
        if (_results.Len > 0)
        {
            AgentLog.Info("application result queue status", ("queuedEvents", _results.Len));
        }
    }

    private async Task PollDeploymentsAsync(CancellationToken cancellationToken)
    {
        if (!_identity.DeploymentOptIn)
        {
            return;
        }
        if (string.IsNullOrEmpty(_deployment.ManifestPublicKeyPath) || string.IsNullOrEmpty(_deployment.DeploymentRoot))
        {
            AgentLog.Warn("deployment work is enabled locally but manifest key or deployment root is not configured");
            return;
        }

        IReadOnlyList<DeploymentRequest> deploys;
        try
        {
            deploys = await _client.GetPendingDeploymentsAsync(_apiKey, cancellationToken) ?? [];
        }
        catch (Exception ex)
        {
            ThrowIfUnauthorized(ex);
            AgentLog.Warn("deployment polling failed", ("error", ex.Message));
            return;
        }

        foreach (var deploy in deploys)
        {
            var result = await DeploymentExecutor.RunDeploymentAsync(_client, _apiKey, _identity, deploy, _deployment, cancellationToken);
            try
            {
                await _client.ReportDeploymentResultAsync(_apiKey, result, cancellationToken);
            }
            catch (Exception ex)
            {
                ThrowIfUnauthorized(ex);
                AgentLog.Warn("deployment result upload failed", ("deploymentId", deploy.Id), ("error", ex.Message));
            }
        }
    }

    private static void ThrowIfUnauthorized(Exception exception)
    {
        if (exception is HttpStatusException { Code: 401 or 403 } status)
        {
            throw new UnauthorizedAgentException($"server rejected the agent identity (HTTP {status.Code})");
        }
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second, DateTimeOffset third) =>
        first < second ? first < third ? first : third : second < third ? second : third;

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
