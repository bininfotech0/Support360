using System.Text.Json;
using Support360.Agent;

namespace Support360.Tests.Agent;

public sealed class AgentRunnerTelemetryRecoveryTests
{
    [Fact]
    public async Task RunRetainsTelemetryDuringOutageAndDrainsAfterRecovery()
    {
        var attempts = 0;
        var acceptedBatch = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new TestHttpServer(context =>
        {
            switch (context.Request.Url!.AbsolutePath)
            {
                case "/api/agent/v1/application-checks":
                    TestHttpServer.RespondRaw(context, "[]");
                    break;
                case "/api/agent/v1/config":
                    TestHttpServer.RespondRaw(context,
                        """{"heartbeatIntervalSeconds":10,"telemetryIntervalSeconds":10,"allowedMetrics":["cpu.usage.percent"]}""");
                    break;
                case "/api/agent/v1/heartbeat":
                    TestHttpServer.Respond(context, 200);
                    break;
                case "/api/agent/v1/telemetry":
                    var body = TestHttpServer.ReadBody<TelemetryBatch>(context);
                    if (Interlocked.Increment(ref attempts) <= 5)
                    {
                        TestHttpServer.Respond(context, 503);
                        return;
                    }
                    TestHttpServer.RespondJson(context, new TelemetryResponse(body.Samples.Count, 0, 0));
                    acceptedBatch.TrySetResult(body.Samples.Count);
                    break;
                default:
                    TestHttpServer.Respond(context, 404);
                    break;
            }
        });

        var queuePath = TestPaths.NewQueuePath("telemetry.json");
        using var client = AgentClient.Create(server.Url, allowHttp: true, _ => TimeSpan.FromMilliseconds(50));
        var runner = new AgentRunner(client,
            new AgentIdentityValue("agent-1", "tenant-1", "test-api-key", false),
            queuePath, new DeploymentRuntimeConfig(null, null));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var runTask = runner.RunAsync(cancellation.Token);

        var winner = await Task.WhenAny(acceptedBatch.Task, runTask, Task.Delay(Timeout.Infinite, cancellation.Token));
        if (winner == runTask)
        {
            await runTask;
            Assert.Fail("agent stopped before telemetry recovered");
        }
        if (winner != acceptedBatch.Task)
        {
            Assert.Fail("timed out waiting for queued telemetry to be accepted after recovery");
        }

        var batchSize = await acceptedBatch.Task;
        Assert.True(batchSize >= 2,
            $"recovery batch contained {batchSize} samples; expected the retained sample plus new telemetry");
        Assert.True(attempts > 5,
            $"telemetry server saw {attempts} attempts; expected outage retries and a recovery upload");

        await Task.Delay(TimeSpan.FromSeconds(1));
        var queued = JsonSerializer.Deserialize<List<MetricSample>>(File.ReadAllText(queuePath), AgentJson.Options);
        Assert.NotNull(queued);
        Assert.Empty(queued);

        cancellation.Cancel();
        var stopped = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(runTask, stopped);
        await runTask;
    }
}

public sealed class AgentRunnerApplicationResultRecoveryTests
{
    [Fact]
    public async Task RunRetainsApplicationResultsDuringOutageAndDrainsAfterRecovery()
    {
        var attempts = 0;
        var acceptedBatch = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        TestHttpServer? server = null;
        server = new TestHttpServer(context =>
        {
            switch (context.Request.Url!.AbsolutePath)
            {
                case "/health":
                    TestHttpServer.Respond(context, 200);
                    break;
                case "/api/agent/v1/application-checks":
                    TestHttpServer.RespondJson(context, new[]
                    {
                        new { id = "check-1", name = "health", type = "Http", target = server!.Url + "/health", intervalSeconds = 30 }
                    });
                    break;
                case "/api/agent/v1/config":
                    TestHttpServer.RespondRaw(context,
                        """{"heartbeatIntervalSeconds":10,"telemetryIntervalSeconds":10,"allowedMetrics":["cpu.usage.percent"]}""");
                    break;
                case "/api/agent/v1/heartbeat":
                    TestHttpServer.Respond(context, 200);
                    break;
                case "/api/agent/v1/telemetry":
                    TestHttpServer.RespondJson(context, new TelemetryResponse(0, 0, 0));
                    break;
                case "/api/agent/v1/application-checks/results":
                    var body = TestHttpServer.ReadBody<ResultsBatch>(context);
                    if (Interlocked.Increment(ref attempts) <= 5)
                    {
                        TestHttpServer.Respond(context, 503);
                        return;
                    }
                    TestHttpServer.RespondJson(context, new ApplicationCheckResultsResponse(body.Results.Count, 0, 0));
                    acceptedBatch.TrySetResult(body.Results.Count);
                    break;
                default:
                    TestHttpServer.Respond(context, 404);
                    break;
            }
        });
        using var _ = server;

        var queuePath = TestPaths.NewQueuePath("telemetry.json");
        using var client = AgentClient.Create(server.Url, allowHttp: true, _ => TimeSpan.FromMilliseconds(50));
        var runner = new AgentRunner(client,
            new AgentIdentityValue("agent-1", "tenant-1", "test-api-key", false),
            queuePath, new DeploymentRuntimeConfig(null, null));

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var runTask = runner.RunAsync(cancellation.Token);

        var winner = await Task.WhenAny(acceptedBatch.Task, runTask, Task.Delay(Timeout.Infinite, cancellation.Token));
        if (winner == runTask)
        {
            await runTask;
            Assert.Fail("agent stopped before application results recovered");
        }
        if (winner != acceptedBatch.Task)
        {
            Assert.Fail("timed out waiting for queued application results to be accepted after recovery");
        }

        var batchSize = await acceptedBatch.Task;
        Assert.True(batchSize >= 1,
            $"recovery batch contained {batchSize} results; expected the retained application result");
        Assert.True(attempts > 5,
            $"results server saw {attempts} attempts; expected outage retries and a recovery upload");

        await Task.Delay(TimeSpan.FromSeconds(1));
        var queued = JsonSerializer.Deserialize<List<ApplicationCheckResult>>(
            File.ReadAllText(queuePath + ".checks"), AgentJson.Options);
        Assert.NotNull(queued);
        Assert.Empty(queued);

        cancellation.Cancel();
        var stopped = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(runTask, stopped);
        await runTask;
    }
}

internal sealed record TelemetryBatch(IReadOnlyList<MetricSample> Samples);

internal sealed record ResultsBatch(IReadOnlyList<ApplicationCheckResult> Results);

internal static class TestPaths
{
    public static string NewQueuePath(string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), "support360-test-runner-" + Guid.NewGuid().ToString("N"), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
