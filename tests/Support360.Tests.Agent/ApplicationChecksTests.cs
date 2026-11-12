using System.Diagnostics;
using Support360.Agent;

namespace Support360.Tests.Agent;

public sealed class ApplicationChecksTests
{
    [Fact]
    public async Task HttpApplicationCheckReportsHealthyAndFailureCategory()
    {
        using var unhealthy = new TestHttpServer(context => TestHttpServer.Respond(context, 503));
        var result = await ApplicationChecks.ExecuteAsync(
            new ApplicationCheck("check-1", "health", "Http", unhealthy.Url, 30), CancellationToken.None);
        Assert.Equal("Unhealthy", result.Status);
        Assert.Equal("HttpStatus", result.ErrorCategory);
        Assert.NotNull(result.ErrorDetail);

        using var healthy = new TestHttpServer(context => TestHttpServer.Respond(context, 204));
        result = await ApplicationChecks.ExecuteAsync(
            new ApplicationCheck("check-1", "health", "Http", healthy.Url, 30), CancellationToken.None);
        Assert.Equal("Healthy", result.Status);
        Assert.NotEqual(string.Empty, result.EventId);
        Assert.Null(result.ErrorCategory);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/health")]
    [InlineData("https://user:pass@example.com/health")]
    [InlineData("/relative/health")]
    [InlineData("http://")]
    [InlineData("https://")]
    [InlineData("")]
    public async Task HttpApplicationCheckRejectsUnsafeTargets(string target)
    {
        var result = await ApplicationChecks.ExecuteAsync(
            new ApplicationCheck("check-1", "health", "Http", target, 30), CancellationToken.None);
        Assert.Equal("Unhealthy", result.Status);
        Assert.Equal("InvalidTarget", result.ErrorCategory);
    }

    [Fact]
    public async Task HttpApplicationCheckSendsUserAgent()
    {
        string? observedUserAgent = null;
        using var server = new TestHttpServer(context =>
        {
            observedUserAgent = context.Request.Headers["User-Agent"];
            TestHttpServer.Respond(context, 204);
        });
        var result = await ApplicationChecks.ExecuteAsync(
            new ApplicationCheck("check-1", "health", "Http", server.Url, 30), CancellationToken.None);
        Assert.Equal("Healthy", result.Status);
        Assert.False(string.IsNullOrWhiteSpace(observedUserAgent), "HTTP check did not send a User-Agent header");
    }

    [Fact]
    public async Task HttpApplicationCheckDoesNotFollowRedirects()
    {
        var redirected = false;
        using var target = new TestHttpServer(context =>
        {
            redirected = true;
            TestHttpServer.Respond(context, 204);
        });
        using var redirector = new TestHttpServer(context =>
        {
            context.Response.StatusCode = 302;
            context.Response.RedirectLocation = target.Url;
        });

        var result = await ApplicationChecks.ExecuteAsync(
            new ApplicationCheck("check-1", "health", "Http", redirector.Url, 30), CancellationToken.None);

        Assert.Equal("Unhealthy", result.Status);
        Assert.Equal("HttpStatus", result.ErrorCategory);
        Assert.False(redirected, "redirect was followed");
    }

    [Fact]
    public async Task HttpApplicationCheckIgnoresEnvironmentProxy()
    {
        var previousHttp = Environment.GetEnvironmentVariable("HTTP_PROXY");
        var previousHttps = Environment.GetEnvironmentVariable("HTTPS_PROXY");
        var previousNo = Environment.GetEnvironmentVariable("NO_PROXY");
        Environment.SetEnvironmentVariable("HTTP_PROXY", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("NO_PROXY", "");
        try
        {
            using var server = new TestHttpServer(context => TestHttpServer.Respond(context, 204));
            var result = await ApplicationChecks.ExecuteAsync(
                new ApplicationCheck("check-1", "health", "Http", server.Url, 30), CancellationToken.None);
            Assert.Equal("Healthy", result.Status);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", previousHttp);
            Environment.SetEnvironmentVariable("HTTPS_PROXY", previousHttps);
            Environment.SetEnvironmentVariable("NO_PROXY", previousNo);
        }
    }

    [Fact]
    public async Task UnknownCheckTypeIsUnsupported()
    {
        var result = await ApplicationChecks.ExecuteAsync(
            new ApplicationCheck("check-1", "mystery", "dnsservice", "example.com", 30), CancellationToken.None);
        Assert.Equal("Unsupported", result.Status);
        Assert.Equal("UnsupportedCheckType", result.ErrorCategory);
    }

    [Fact]
    public async Task ProcessCheckReportsPresentForCurrentProcess()
    {
        var expected = Process.GetCurrentProcess().ProcessName;
        var result = await ApplicationChecks.ExecuteAsync(
            new ApplicationCheck("check-1", "proc", "process", expected, 30), CancellationToken.None);
        Assert.Equal("Present", result.Status);
    }

    [Fact]
    public async Task WindowsServiceCheckQueriesRealServiceOnWindows()
    {
        var result = await ApplicationChecks.ExecuteAsync(
            new ApplicationCheck("check-1", "svc", "windowsservice", "Spooler", 30), CancellationToken.None);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("Unsupported", result.Status);
            Assert.Equal("PlatformUnsupported", result.ErrorCategory);
        }
        else
        {
            Assert.NotEqual("Unsupported", result.Status);
            Assert.True(result.Status == "Healthy"
                || result.ErrorCategory is "ServiceNotRunning" or "ServiceQueryFailed",
                $"unexpected service query result: {result.Status} {result.ErrorCategory}");
        }
    }
}
