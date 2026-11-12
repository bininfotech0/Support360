using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Support360.Agent;

public static class ApplicationChecks
{
    private const int MaximumTargetLength = 2048;

    private static readonly Lazy<HttpClient> Http = new(CreateHttpClient);

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxConnectionsPerServer = 4,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.None
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestHeaders = { UserAgent = { new ProductInfoHeaderValue("support360-agent", AgentClient.Version) } }
        };
    }

    public static async Task<ApplicationCheckResult> ExecuteAsync(ApplicationCheck check, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var (status, category, detail) = check.Type.ToLowerInvariant() switch
        {
            "http" => await ExecuteHttpCheck(check, cancellationToken),
            "process" => ExecuteProcessCheck(check),
            "systemdservice" => ExecuteSystemdCheck(check, cancellationToken),
            "windowsservice" => ExecuteWindowsServiceCheck(check, cancellationToken),
            _ => ("Unsupported", "UnsupportedCheckType", "unsupported check type")
        };

        var finished = DateTimeOffset.UtcNow;
        string eventId;
        try
        {
            eventId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        }
        catch (CryptographicException)
        {
            eventId = "fallback-" + finished.ToUnixTimeMilliseconds();
        }
        return new ApplicationCheckResult(check.Id, eventId, status, started, finished,
            (long)(finished - started).TotalMilliseconds, EmptyToNull(category), EmptyToNull(detail));
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static async Task<(string Status, string Category, string Detail)> ExecuteHttpCheck(ApplicationCheck check, CancellationToken cancellationToken)
    {
        var invalid = ValidateHttpTarget(check.Target);
        if (invalid is not null)
        {
            return ("Unhealthy", "InvalidTarget", invalid);
        }

        try
        {
            using var response = await Http.Value.GetAsync(check.Target, cancellationToken);
            if ((int)response.StatusCode is >= 200 and < 300)
            {
                return ("Healthy", string.Empty, string.Empty);
            }
            var phrase = string.IsNullOrEmpty(response.ReasonPhrase) ? response.StatusCode.ToString() : response.ReasonPhrase;
            return ("Unhealthy", "HttpStatus", $"{(int)response.StatusCode} {phrase}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return ("Unhealthy", "ConnectionFailed", ex.Message);
        }
    }

    internal static string? ValidateHttpTarget(string target)
    {
        if (target.Length > MaximumTargetLength)
        {
            return "target exceeds 2048 characters";
        }
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return "target must be an absolute HTTP(S) URL without embedded credentials";
        }
        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme != "http" && scheme != "https")
        {
            return "target must be an absolute HTTP(S) URL without embedded credentials";
        }
        return null;
    }

    private static (string, string, string) ExecuteProcessCheck(ApplicationCheck check)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            return ("Unhealthy", "ProcessQueryFailed", ex.Message);
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (string.Equals(TrimExecutableSuffix(process.ProcessName), TrimExecutableSuffix(check.Target), StringComparison.OrdinalIgnoreCase))
                    {
                        return ("Present", string.Empty, string.Empty);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (Win32Exception)
                {
                }
                catch (IOException)
                {
                }
            }
        }
        return ("Unhealthy", "CheckFailed", "check did not succeed");
    }

    private static string TrimExecutableSuffix(string value) =>
        value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;

    private static (string, string, string) ExecuteSystemdCheck(ApplicationCheck check, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return ("Unsupported", "PlatformUnsupported", "systemd checks require Linux");
        }
        if (FindOnPath("systemctl") is null)
        {
            return ("Unsupported", "CheckDependencyMissing", "systemctl is not installed on this host");
        }
        if (check.Target.IndexOfAny([' ', '/', '\\', '\t', '\r', '\n']) >= 0)
        {
            return ("Unhealthy", "InvalidTarget", "invalid service name");
        }

        var output = RunProcess("systemctl", ["show", "--property=ActiveState", "--value", check.Target], cancellationToken, out var exitCode);
        var trimmed = output.Trim();
        if (exitCode != 0)
        {
            return ("Unhealthy", "ServiceQueryFailed", trimmed);
        }
        return trimmed == "active"
            ? ("Healthy", string.Empty, string.Empty)
            : ("Unhealthy", "ServiceInactive", trimmed);
    }

    private static (string, string, string) ExecuteWindowsServiceCheck(ApplicationCheck check, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ("Unsupported", "PlatformUnsupported", "Windows service checks require Windows");
        }
        if (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        try
        {
            using var controller = new System.ServiceProcess.ServiceController(check.Target);
            var state = controller.Status switch
            {
                System.ServiceProcess.ServiceControllerStatus.Running => "running",
                System.ServiceProcess.ServiceControllerStatus.StartPending => "start-pending",
                System.ServiceProcess.ServiceControllerStatus.StopPending => "stop-pending",
                System.ServiceProcess.ServiceControllerStatus.Stopped => "stopped",
                _ => $"state-{(int)controller.Status}"
            };
            return state == "running"
                ? ("Healthy", string.Empty, string.Empty)
                : ("Unhealthy", "ServiceNotRunning", state);
        }
        catch (InvalidOperationException ex)
        {
            return ("Unhealthy", "ServiceQueryFailed", ex.Message);
        }
    }

    private static string RunProcess(string fileName, string[] arguments, CancellationToken cancellationToken, out int exitCode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"could not start {fileName}");
        var outputTask = Task.WhenAll(process.StandardOutput.ReadToEndAsync(cancellationToken), process.StandardError.ReadToEndAsync(cancellationToken));
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
        }
        outputTask.Wait(cancellationToken);
        var outputs = outputTask.Result;
        exitCode = process.ExitCode;
        return string.Concat(outputs);
    }

    private static string? FindOnPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (!File.Exists(candidate))
                {
                    continue;
                }
                if (OperatingSystem.IsWindows())
                {
                    return candidate;
                }
                var mode = File.GetUnixFileMode(candidate);
                if ((mode & UnixFileMode.UserExecute) != 0 || (mode & UnixFileMode.GroupExecute) != 0 || (mode & UnixFileMode.OtherExecute) != 0)
                {
                    return candidate;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return null;
    }
}
