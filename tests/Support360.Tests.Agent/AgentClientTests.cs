using Support360.Agent;

namespace Support360.Tests.Agent;

public sealed class AgentClientTests
{
    [Fact]
    public void ClientRequiresHttpsByDefault()
    {
        Assert.ThrowsAny<ArgumentException>(() => AgentClient.Create("http://localhost:8080", allowHttp: false));
        using var _ = AgentClient.Create("http://localhost:8080", allowHttp: true);
        using var __ = AgentClient.Create("https://support360.example", allowHttp: false);
        foreach (var rawUrl in new[]
                 {
                     "https://user:pass@support360.example",
                     "https://support360.example/?token=x",
                     "https://support360.example/#fragment",
                     "",
                     "support360.example"
                 })
        {
            Assert.ThrowsAny<ArgumentException>(() => AgentClient.Create(rawUrl, allowHttp: false));
        }
    }

    [Fact]
    public void DeploymentCapabilityRequiresExplicitLocalOptIn()
    {
        Assert.DoesNotContain("deployment_execution", AgentPlatformInfo.Capabilities(false));
        Assert.Contains("deployment_execution", AgentPlatformInfo.Capabilities(true));
    }

    [Fact]
    public async Task EnrollmentPersistsDeploymentOptInLocally()
    {
        using var server = new TestHttpServer(context =>
        {
            if (context.Request.Url!.AbsolutePath != "/api/agent/v1/enroll")
            {
                TestHttpServer.Respond(context, 404);
                return;
            }
            var request = TestHttpServer.ReadBody<EnrollRequest>(context);
            _optInSeen = request.Capabilities.Contains("deployment_execution");
            TestHttpServer.RespondJson(context, new Dictionary<string, object>
            {
                ["agentId"] = "agent-1",
                ["tenantId"] = "tenant-1",
                ["apiKey"] = "test-agent-api-key",
                ["heartbeatIntervalSeconds"] = 30,
                ["telemetryIntervalSeconds"] = 60,
                ["allowedMetrics"] = new[] { "cpu.usage.percent" },
                ["serverTime"] = DateTimeOffset.UtcNow
            });
        });

        var identityPath = Path.Combine(Path.GetTempPath(), "support360-test-identity-" + Guid.NewGuid().ToString("N"), "identity.json");
        using (var client = AgentClient.Create(server.Url, allowHttp: true))
        {
            await client.EnrollAsync("single-use-token", identityPath, deploymentOptIn: true, CancellationToken.None);
        }

        Assert.True(_optInSeen, "enrollment did not advertise the explicit local deployment opt-in");

        var identity = AgentIdentity.Read(identityPath);
        Assert.True(identity.DeploymentOptIn, "deployment opt-in was not persisted in the protected identity file");
        Assert.Equal("agent-1", identity.AgentId);
        Assert.Equal("tenant-1", identity.TenantId);
    }

    [Fact]
    public void EnrollmentRejectsIncompleteIdentity()
    {
        using var server = new TestHttpServer(context =>
        {
            TestHttpServer.RespondRaw(context, 200, """{"agentId":"agent-1","tenantId":"tenant-1"}""");
        });

        var identityPath = Path.Combine(Path.GetTempPath(), "support360-test-empty-" + Guid.NewGuid().ToString("N"), "identity.json");
        Directory.CreateDirectory(Path.GetDirectoryName(identityPath)!);
        using var client = AgentClient.Create(server.Url, allowHttp: true);
        var error = Assert.Throws<InvalidOperationException>(() =>
            client.EnrollAsync("token", identityPath, false, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Contains("identity", error.Message);
    }

    private bool _optInSeen;
}
