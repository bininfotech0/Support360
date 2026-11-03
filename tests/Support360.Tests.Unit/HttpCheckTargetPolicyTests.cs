using Support360.Application.Security;

namespace Support360.Tests.Unit;

public sealed class HttpCheckTargetPolicyTests
{
    [Fact]
    public void EmptyPolicyAllowsEveryHttpTarget()
    {
        Assert.True(HttpCheckTargetPolicy.IsAllowed("https://anything.example/health", []));
    }

    [Theory]
    [InlineData("https://api.example.com/health", "api.example.com", true)]
    [InlineData("https://API.EXAMPLE.COM./health", "api.example.com", true)]
    [InlineData("https://api.example.com.evil.net/health", "api.example.com", false)]
    [InlineData("http://api.example.com:8080/health", "api.example.com", true)]
    [InlineData("https://example.com/health", "api.example.com", false)]
    public void ExactHostPatternMatchesOnlyTheConfiguredHost(string target, string pattern, bool expected)
    {
        Assert.Equal(expected, HttpCheckTargetPolicy.IsAllowed(target, [pattern]));
    }

    [Theory]
    [InlineData("https://app.internal.example.com/health", "*.internal.example.com", true)]
    [InlineData("https://internal.example.com/health", "*.internal.example.com", false)]
    [InlineData("https://evil-internal.example.com/health", "*.internal.example.com", false)]
    public void WildcardPatternMatchesSubdomainsOnly(string target, string pattern, bool expected)
    {
        Assert.Equal(expected, HttpCheckTargetPolicy.IsAllowed(target, [pattern]));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080/health", "127.0.0.1", true)]
    [InlineData("http://127.0.0.2:8080/health", "127.0.0.1", false)]
    [InlineData("http://[::1]:8080/health", "::1", true)]
    public void IpPatternsMatchExactAddresses(string target, string pattern, bool expected)
    {
        Assert.Equal(expected, HttpCheckTargetPolicy.IsAllowed(target, [pattern]));
    }

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("*.example.com", true)]
    [InlineData("sub.example.com.", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("*.127.0.0.1", false)]
    [InlineData("*", false)]
    [InlineData("**", false)]
    [InlineData("example.com/path", false)]
    [InlineData("https://example.com", false)]
    [InlineData("user@example.com", false)]
    [InlineData("", false)]
    public void ValidatesAndNormalizesConfiguredPatterns(string pattern, bool expected)
    {
        Assert.Equal(expected, HttpCheckTargetPolicy.IsValidPattern(pattern));
    }

    [Fact]
    public void NormalizesHostsToLowercaseAndDeduplicates()
    {
        var normalized = HttpCheckTargetPolicy.Normalize(
            [" API.Example.com. ", "api.example.com", "*.Internal.Example.com", "127.0.0.1"]);

        Assert.Equal(["api.example.com", "*.internal.example.com", "127.0.0.1"], normalized);
    }
}
