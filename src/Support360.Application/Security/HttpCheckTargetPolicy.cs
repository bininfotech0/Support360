using System.Net;

namespace Support360.Application.Security;

public static class HttpCheckTargetPolicy
{
    public const int MaxAllowedHosts = 50;
    public const int MaxHostLength = 255;

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? hosts)
    {
        if (hosts is null)
        {
            return [];
        }

        var normalized = new List<string>();
        foreach (var host in hosts)
        {
            var value = NormalizePattern(host);
            if (!normalized.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(value);
            }
        }

        return normalized;
    }

    public static string NormalizePattern(string raw)
    {
        var value = raw.Trim().TrimEnd('.').ToLowerInvariant();
        return value.StartsWith("*.", StringComparison.Ordinal) ? "*." + value[2..] : value;
    }

    public static bool IsValidPattern(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var value = NormalizePattern(raw);
        if (value.Length is 0 or > MaxHostLength)
        {
            return false;
        }

        var wildcard = value.StartsWith("*.", StringComparison.Ordinal);
        var host = wildcard ? value[2..] : value;
        if (host.Length == 0 || host.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out _))
        {
            return !wildcard;
        }

        return Uri.CheckHostName(host) == UriHostNameType.Dns;
    }

    public static bool IsAllowed(string target, IReadOnlyCollection<string> allowedHosts)
    {
        if (allowedHosts.Count == 0)
        {
            return true;
        }

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (IPAddress.TryParse(host.Trim('[', ']'), out var targetAddress))
        {
            return allowedHosts
                .Where(pattern => !pattern.StartsWith("*.", StringComparison.Ordinal))
                .Select(pattern => pattern.Trim('[', ']'))
                .Any(pattern => IPAddress.TryParse(pattern, out var allowed) && allowed.Equals(targetAddress));
        }

        foreach (var pattern in allowedHosts)
        {
            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                if (host.EndsWith(pattern[1..], StringComparison.Ordinal))
                {
                    return true;
                }

                continue;
            }

            if (string.Equals(host, pattern, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
