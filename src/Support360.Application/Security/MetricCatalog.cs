namespace Support360.Application.Security;

public static class MetricCatalog
{
    public const string CpuUsagePercent = "cpu.usage.percent";
    public const string MemoryUsedPercent = "memory.used.percent";
    public const string MemoryUsedBytes = "memory.used.bytes";
    public const string DiskUsedPercent = "disk.used.percent";
    public const string DiskFreeBytes = "disk.free.bytes";
    public const string NetworkRxBytesTotal = "network.rx.bytes_total";
    public const string NetworkTxBytesTotal = "network.tx.bytes_total";
    public const string UptimeSeconds = "uptime.seconds";

    public static readonly IReadOnlyDictionary<string, string> Units =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CpuUsagePercent] = "percent",
            [MemoryUsedPercent] = "percent",
            [MemoryUsedBytes] = "bytes",
            [DiskUsedPercent] = "percent",
            [DiskFreeBytes] = "bytes",
            [NetworkRxBytesTotal] = "bytes",
            [NetworkTxBytesTotal] = "bytes",
            [UptimeSeconds] = "seconds"
        };

    public static readonly IReadOnlyList<string> Allowed = Units.Keys.Order(StringComparer.Ordinal).ToArray();
}
