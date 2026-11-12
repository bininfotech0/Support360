using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Support360.Agent;

public static class AgentPlatformInfo
{
    public static string Name => OperatingSystem.IsWindows()
        ? "Windows"
        : OperatingSystem.IsLinux()
            ? "Linux"
            : throw new PlatformNotSupportedException("the Support360 agent supports Windows and Linux hosts only");

    public static string Architecture => RuntimeInformation.OSArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.X86 => "386",
        System.Runtime.InteropServices.Architecture.X64 => "amd64",
        System.Runtime.InteropServices.Architecture.Arm => "arm",
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
    };

    public static IReadOnlyList<string> Capabilities(bool deploymentOptIn)
    {
        var capabilities = new List<string> { "metrics", "heartbeat", "application_checks", "process_checks" };
        if (deploymentOptIn)
        {
            capabilities.Add("deployment_execution");
        }
        if (OperatingSystem.IsWindows())
        {
            capabilities.Add("windows_service_checks");
        }
        else if (OperatingSystem.IsLinux())
        {
            capabilities.Add("systemd_service_checks");
        }
        return capabilities;
    }
}

public readonly record struct HostDetails(string Hostname, string OsVersion, string Architecture, long UptimeSeconds);

public sealed class HostCollector
{
    private readonly TimeSpan _cpuSampleInterval;

    public HostCollector() : this(TimeSpan.FromSeconds(1))
    {
    }

    public HostCollector(TimeSpan cpuSampleInterval)
    {
        _cpuSampleInterval = cpuSampleInterval;
    }

    public async Task<IReadOnlyList<MetricSample>> CollectAsync(CancellationToken cancellationToken)
    {
        var captured = DateTimeOffset.UtcNow;
        var samples = new List<MetricSample>(8);

        void Add(string name, double value, string unit)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                throw new InvalidOperationException($"collector returned invalid value for {name}");
            }
            samples.Add(new MetricSample(NewEventId(), name, value, unit, captured));
        }

        var cpuPercent = await CpuPercentAsync(cancellationToken).ConfigureAwait(false);
        Add("cpu.usage.percent", cpuPercent, "percent");

        var (memoryUsedBytes, memoryTotalBytes) = ReadMemory();
        Add("memory.used.percent", 100.0 * memoryUsedBytes / memoryTotalBytes, "percent");
        Add("memory.used.bytes", memoryUsedBytes, "bytes");

        var drive = new DriveInfo(SystemDiskPath());
        var diskTotal = (double)drive.TotalSize;
        var diskFree = (double)drive.TotalFreeSpace;
        Add("disk.used.percent", 100.0 * (diskTotal - diskFree) / diskTotal, "percent");
        Add("disk.free.bytes", diskFree, "bytes");

        var (bytesReceived, bytesSent) = ReadNetworkTotals();
        Add("network.rx.bytes_total", bytesReceived, "bytes");
        Add("network.tx.bytes_total", bytesSent, "bytes");

        Add("uptime.seconds", UptimeSeconds(), "seconds");

        return samples;
    }

    public static HostDetails CaptureHostDetails()
    {
        var hostname = "unknown-host";
        try
        {
            hostname = Dns.GetHostName();
        }
        catch (SocketException)
        {
        }
        if (string.IsNullOrWhiteSpace(hostname))
        {
            hostname = "unknown-host";
        }

        var osVersion = RuntimeInformation.OSDescription.Trim();
        if (string.IsNullOrEmpty(osVersion))
        {
            osVersion = Environment.OSVersion.ToString();
        }

        return new HostDetails(hostname, osVersion, AgentPlatformInfo.Architecture, UptimeSeconds());
    }

    private static string SystemDiskPath() => OperatingSystem.IsWindows() ? @"C:\" : "/";

    private static long UptimeSeconds()
    {
        if (OperatingSystem.IsWindows())
        {
            return Environment.TickCount64 / 1000;
        }
        var line = File.ReadLines("/proc/uptime").FirstOrDefault();
        if (line is null || !double.TryParse(line.AsSpan(0, line.IndexOf(' ') is < 0 ? line.Length : line.IndexOf(' ')), NumberStyles.Float, CultureInfo.InvariantCulture, out var uptime))
        {
            throw new InvalidOperationException("could not read host uptime");
        }
        return (long)uptime;
    }

    private async Task<double> CpuPercentAsync(CancellationToken cancellationToken)
    {
        var before = ReadCpuTotals();
        await Task.Delay(_cpuSampleInterval, cancellationToken).ConfigureAwait(false);
        var after = ReadCpuTotals();

        var totalDelta = after.Total - before.Total;
        var idleDelta = after.Idle - before.Idle;
        if (totalDelta <= 0)
        {
            throw new InvalidOperationException("could not compute CPU utilization");
        }
        return Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
    }

    private static (double Idle, double Total) ReadCpuTotals()
    {
        if (OperatingSystem.IsWindows())
        {
            if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                throw new InvalidOperationException("could not sample system CPU times");
            }
            var idleValue = ToUInt64(idleTime);
            var kernelValue = ToUInt64(kernelTime);
            var userValue = ToUInt64(userTime);
            return (idleValue, kernelValue + userValue);
        }

        var line = File.ReadLines("/proc/stat").FirstOrDefault();
        if (line is null || !line.StartsWith("cpu ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("could not read /proc/stat");
        }
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 5)
        {
            throw new InvalidOperationException("could not parse /proc/stat");
        }

        double idle = 0, total = 0;
        for (var index = 1; index < fields.Length; index++)
        {
            if (!long.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException("could not parse /proc/stat");
            }
            total += value;
            if (index is 4 or 5)
            {
                idle += value;
            }
        }
        return (idle, total);
    }

    private static (double UsedBytes, double TotalBytes) ReadMemory()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(status))
            {
                throw new InvalidOperationException("could not sample system memory");
            }
            var total = (double)status.ullTotalPhys;
            var available = (double)status.ullAvailPhys;
            if (total <= 0)
            {
                throw new InvalidOperationException("could not sample system memory");
            }
            return (total - available, total);
        }

        double? totalKb = null;
        double? availableKb = null;
        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }
            var key = line[..separator].Trim();
            var valuePart = line[(separator + 1)..].Trim();
            var trailing = valuePart.IndexOf(' ');
            var numberText = trailing < 0 ? valuePart : valuePart[..trailing];
            if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }
            if (key == "MemTotal")
            {
                totalKb = value;
            }
            else if (key == "MemAvailable")
            {
                availableKb = value;
            }
        }

        if (totalKb is not > 0 || availableKb is not >= 0)
        {
            throw new InvalidOperationException("could not read /proc/meminfo");
        }
        var totalBytes = totalKb.Value * 1024;
        return (totalBytes - availableKb.Value * 1024, totalBytes);
    }

    private static (double BytesReceived, double BytesSent) ReadNetworkTotals()
    {
        double received = 0;
        double sent = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                var statistics = nic.GetIPv4Statistics();
                if (statistics is null)
                {
                    continue;
                }
                received += statistics.BytesReceived;
                sent += statistics.BytesSent;
            }
            catch (InvalidOperationException)
            {
            }
            catch (NetworkInformationException)
            {
            }
        }
        return (received, sent);
    }

    private static string NewEventId()
    {
        // A timestamp plus random bytes keeps IDs unique across process restarts.
        return DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss.fffffff", CultureInfo.InvariantCulture)
            + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

    private static ulong ToUInt64(FILETIME value) => (ulong)value.HighDateTime << 32 | value.LowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}
