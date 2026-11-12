using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Support360.Agent;

public sealed class HttpStatusException : Exception
{
    public int Code { get; }

    public HttpStatusException(int code) : base($"server returned HTTP {code}")
    {
        Code = code;
    }
}

public sealed class UnauthorizedAgentException : Exception
{
    public UnauthorizedAgentException(string message) : base(message)
    {
    }
}

public sealed class AgentClient : IDisposable
{
    public const string Version = "0.3.0";

    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private const int MaxArtifactBytes = 20 * 1024 * 1024;
    private const int RetryAttempts = 5;

    private readonly Uri _baseUrl;
    private readonly HttpClient _http;
    private readonly Func<int, TimeSpan> _retryDelay;

    private AgentClient(Uri baseUrl, Func<int, TimeSpan> retryDelay)
    {
        _baseUrl = baseUrl;
        _retryDelay = retryDelay;
        _http = new HttpClient(new SocketsHttpHandler())
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public static AgentClient Create(string? rawUrl, bool allowHttp, Func<int, TimeSpan>? retryDelay = null)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            throw new ArgumentException("server URL is required");
        }

        var trimmed = rawUrl.TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("server URL must be absolute and must not contain credentials, a query, or a fragment");
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme != "https" && !(allowHttp && scheme == "http"))
        {
            throw new ArgumentException("HTTPS is required; HTTP is allowed only with the explicit development flag");
        }

        return new AgentClient(uri, retryDelay ?? DefaultRetryDelay);
    }

    public void Dispose() => _http.Dispose();

    public async Task<EnrollResponse> EnrollAsync(string token, string identityPath, bool deploymentOptIn, CancellationToken cancellationToken)
    {
        var details = HostCollector.CaptureHostDetails();
        var request = new EnrollRequest(
            token,
            details.Hostname,
            AgentPlatformInfo.Name,
            details.OsVersion,
            details.Architecture,
            Version,
            AgentPlatformInfo.Capabilities(deploymentOptIn));

        var result = await RequestAsync<EnrollResponse>(HttpMethod.Post, "/api/agent/v1/enroll", apiKey: null, request, retries: false, cancellationToken)
            ?? throw new InvalidOperationException("enrollment response did not include an agent identity");

        if (string.IsNullOrEmpty(result.AgentId) || string.IsNullOrEmpty(result.TenantId) || string.IsNullOrEmpty(result.ApiKey))
        {
            throw new InvalidOperationException("enrollment response did not include an agent identity");
        }

        try
        {
            AgentIdentity.Write(identityPath, new AgentIdentityValue(result.AgentId, result.TenantId, result.ApiKey, deploymentOptIn));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("save agent identity: " + ex.Message, ex);
        }
        return result;
    }

    public Task<AgentConfig?> GetConfigAsync(string apiKey, CancellationToken cancellationToken) =>
        RequestAsync<AgentConfig>(HttpMethod.Get, "/api/agent/v1/config", apiKey, null, retries: true, cancellationToken);

    public Task HeartbeatAsync(string apiKey, HeartbeatRequest body, CancellationToken cancellationToken) =>
        RequestVoidAsync(HttpMethod.Post, "/api/agent/v1/heartbeat", apiKey, body, retries: true, cancellationToken);

    public Task<TelemetryResponse?> SendTelemetryAsync(string apiKey, IReadOnlyList<MetricSample> batch, CancellationToken cancellationToken) =>
        RequestAsync<TelemetryResponse>(HttpMethod.Post, "/api/agent/v1/telemetry", apiKey, new { samples = batch }, retries: true, cancellationToken);

    public Task<IReadOnlyList<ApplicationCheck>?> GetApplicationChecksAsync(string apiKey, CancellationToken cancellationToken) =>
        RequestAsync<IReadOnlyList<ApplicationCheck>>(HttpMethod.Get, "/api/agent/v1/application-checks", apiKey, null, retries: true, cancellationToken);

    public Task<ApplicationCheckResultsResponse?> SendApplicationCheckResultsAsync(string apiKey, IReadOnlyList<ApplicationCheckResult> batch, CancellationToken cancellationToken) =>
        RequestAsync<ApplicationCheckResultsResponse>(HttpMethod.Post, "/api/agent/v1/application-checks/results", apiKey, new { results = batch }, retries: true, cancellationToken);

    public Task<IReadOnlyList<DeploymentRequest>?> GetPendingDeploymentsAsync(string apiKey, CancellationToken cancellationToken) =>
        RequestAsync<IReadOnlyList<DeploymentRequest>>(HttpMethod.Get, "/api/agent/v1/deployments/pending", apiKey, null, retries: true, cancellationToken);

    public Task<DeploymentManifest?> GetDeploymentManifestAsync(string apiKey, string deploymentId, CancellationToken cancellationToken) =>
        RequestAsync<DeploymentManifest>(HttpMethod.Get, "/api/agent/v1/deployments/" + Uri.EscapeDataString(deploymentId) + "/manifest", apiKey, null, retries: false, cancellationToken);

    public Task ReportDeploymentResultAsync(string apiKey, DeploymentResult result, CancellationToken cancellationToken) =>
        RequestVoidAsync(HttpMethod.Post, "/api/agent/v1/deployments/result", apiKey, result, retries: true, cancellationToken);

    public async Task<byte[]> DownloadDeploymentArtifactAsync(string apiKey, string deploymentId, CancellationToken cancellationToken)
    {
        var path = "/api/agent/v1/deployments/" + Uri.EscapeDataString(deploymentId) + "/artifact";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUrl, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await DrainAsync(response);
            throw new HttpStatusException((int)response.StatusCode);
        }

        using var buffer = new MemoryStream();
        using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            if (buffer.Length > MaxArtifactBytes)
            {
                throw new InvalidOperationException("deployment artifact exceeds the 20 MiB limit");
            }
        }
        return buffer.ToArray();
    }

    private async Task<T?> RequestAsync<T>(HttpMethod method, string path, string? apiKey, object? body, bool retries, CancellationToken cancellationToken)
    {
        var payload = await SendAsync(method, path, apiKey, body, retries, cancellationToken);
        if (payload is null || payload.Length == 0)
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(payload, AgentJson.Options);
    }

    private async Task RequestVoidAsync(HttpMethod method, string path, string? apiKey, object? body, bool retries, CancellationToken cancellationToken)
    {
        await SendAsync(method, path, apiKey, body, retries, cancellationToken);
    }

    private async Task<byte[]?> SendAsync(HttpMethod method, string path, string? apiKey, object? body, bool retries, CancellationToken cancellationToken)
    {
        byte[]? json = null;
        if (body is not null)
        {
            json = JsonSerializer.SerializeToUtf8Bytes(body, AgentJson.Options);
        }

        var attempts = retries ? RetryAttempts : 1;
        Exception? last = null;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(method, new Uri(_baseUrl, path));
                request.Headers.Accept.ParseAdd("application/json");
                if (json is not null)
                {
                    request.Content = new ByteArrayContent(json);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                }
                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var payload = await ReadBoundedAsync(response, cancellationToken);
                var status = (int)response.StatusCode;
                if (status is >= 200 and < 300)
                {
                    return payload;
                }
                var statusError = new HttpStatusException(status);
                if (status < 500 && status != (int)HttpStatusCode.TooManyRequests)
                {
                    throw statusError;
                }
                last = statusError;
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                last = new TimeoutException("request timed out", ex);
            }
            catch (IOException ex)
            {
                last = ex;
            }

            if (attempt + 1 < attempts)
            {
                await Task.Delay(_retryDelay(attempt), cancellationToken);
            }
        }

        if (last is null)
        {
            throw new InvalidOperationException("request failed without a captured error");
        }
        throw last;
    }

    private static async Task<byte[]?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            if (buffer.Length > MaxResponseBytes)
            {
                return buffer.ToArray();
            }
        }
        return buffer.ToArray();
    }

    private static async Task DrainAsync(HttpResponseMessage response)
    {
        try
        {
            using var source = await response.Content.ReadAsStreamAsync(CancellationToken.None);
            var chunk = new byte[64 * 1024];
            var drained = 0L;
            while (drained <= 64 * 1024)
            {
                var read = await source.ReadAsync(chunk, CancellationToken.None);
                if (read == 0)
                {
                    break;
                }
                drained += read;
            }
        }
        catch (IOException)
        {
        }
        catch (HttpRequestException)
        {
        }
    }

    private static TimeSpan DefaultRetryDelay(int attempt)
    {
        var shift = Math.Min(attempt, 10);
        return TimeSpan.FromSeconds(1L << shift) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
    }
}
