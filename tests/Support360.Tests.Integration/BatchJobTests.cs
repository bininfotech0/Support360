using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Support360.Contracts.Agents;
using Support360.Contracts.Auth;
using Support360.Contracts.Dashboard;
using Support360.Contracts.Jobs;
using Support360.Contracts.Telemetry;

namespace Support360.Tests.Integration;

public sealed class BatchJobTests : IClassFixture<Support360ApiFactory>
{
    private readonly Support360ApiFactory _factory;
    public BatchJobTests(Support360ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task AdminCanConfigureJobsAndAgentEventsPreserveConfiguredSchedule()
    {
        using var user = _factory.CreateClient();
        var login = await user.PostAsJsonAsync("/api/auth/login", new LoginRequest(Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword));
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var enrollmentResponse = await user.PostAsJsonAsync("/api/enrollments", new CreateEnrollmentRequest("Linux"));
        enrollmentResponse.EnsureSuccessStatusCode();
        var enrollment = (await enrollmentResponse.Content.ReadFromJsonAsync<EnrollmentCreatedResponse>())!;

        using var agent = _factory.CreateClient();
        var enroll = await agent.PostAsJsonAsync("/api/agent/v1/enroll", new EnrollAgentRequest(
            enrollment.Token, $"batch-{Guid.NewGuid():N}"[..20], "Linux", "Ubuntu 24.04", "amd64", "0.1.0", ["metrics"]));
        enroll.EnsureSuccessStatusCode();
        var identity = (await enroll.Content.ReadFromJsonAsync<EnrollAgentResponse>())!;
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.ApiKey);

        var configuredResponse = await user.PostAsJsonAsync("/api/batch-jobs", new ConfigureBatchJobRequest(
            identity.AgentId, "nightly-ledger", "Nightly ledger close", "Quartz", "Weekdays at 02:00 UTC"));
        Assert.Equal(HttpStatusCode.Created, configuredResponse.StatusCode);
        var configured = (await configuredResponse.Content.ReadFromJsonAsync<BatchJobDto>())!;
        Assert.Null(configured.LastRunAt);

        var duplicate = await user.PostAsJsonAsync("/api/batch-jobs", new ConfigureBatchJobRequest(
            identity.AgentId, "nightly-ledger", "Duplicate", null, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var now = DateTimeOffset.UtcNow;
        var eventResponse = await agent.PostAsJsonAsync("/api/agent/v1/job-events", new JobEventBatchRequest(
        [new JobEventDto("run-" + Guid.NewGuid().ToString("N"), "nightly-ledger", "Agent supplied name", "agent-scheduler",
            now.AddMinutes(-2), now, "Failed", 23, "Ledger source unavailable")]));
        eventResponse.EnsureSuccessStatusCode();
        var accepted = (await eventResponse.Content.ReadFromJsonAsync<JobEventsResponse>())!;
        Assert.Equal(1, accepted.Accepted);

        var jobs = await user.GetFromJsonAsync<List<BatchJobDto>>("/api/batch-jobs");
        var observed = Assert.Single(jobs!, j => j.Id == configured.Id);
        Assert.Equal("Nightly ledger close", observed.Name);
        Assert.Equal("Quartz", observed.Scheduler);
        Assert.Equal("Weekdays at 02:00 UTC", observed.ExpectedSchedule);
        var execution = Assert.Single(observed.RecentExecutions);
        Assert.Equal("Failed", execution.Status);
        Assert.Equal(23, execution.ExitCode);
        Assert.Equal("Ledger source unavailable", execution.FailureReason);

        var wrongAgent = await user.PostAsJsonAsync("/api/batch-jobs", new ConfigureBatchJobRequest(
            Guid.NewGuid(), "foreign-job", "Foreign", null, null));
        Assert.Equal(HttpStatusCode.NotFound, wrongAgent.StatusCode);
    }
}
