namespace Support360.Agent;

internal static class AgentPaths
{
    public static string DefaultIdentityPath() =>
        Environment.GetEnvironmentVariable("SUPPORT360_IDENTITY_FILE") is { Length: > 0 } identity
            ? identity
            : Path.Combine(UserConfigDirectory(), "support360", "identity.json");

    public static string DefaultQueuePath() =>
        Environment.GetEnvironmentVariable("SUPPORT360_QUEUE_FILE") is { Length: > 0 } queue
            ? queue
            : Path.Combine(UserCacheDirectory(), "support360", "queue.json");

    private static string UserConfigDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return string.IsNullOrEmpty(appData) ? "." : appData;
        }
        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfig) && Path.IsPathRooted(xdgConfig))
        {
            return xdgConfig;
        }
        var home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrEmpty(home) ? "." : Path.Combine(home, ".config");
    }

    private static string UserCacheDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(localAppData) ? "." : localAppData;
        }
        var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrEmpty(xdgCache) && Path.IsPathRooted(xdgCache))
        {
            return xdgCache;
        }
        var home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrEmpty(home) ? "." : Path.Combine(home, ".cache");
    }
}
