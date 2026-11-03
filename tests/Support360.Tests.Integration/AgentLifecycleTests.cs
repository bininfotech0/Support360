using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Support360.Application.Abstractions;
using Support360.Contracts.Agents;
using Support360.Contracts.Applications;
using Support360.Contracts.Auth;
using Support360.Contracts.Dashboard;
using Support360.Contracts.Telemetry;
using Support360.Domain;
using Support360.Domain.Entities;
using Support360.Infrastructure.Persistence;

namespace Support360.Tests.Integration;

public sealed class AgentLifecycleTests : IClassFixture<Support360ApiFactory>
{
    private readonly Support360ApiFactory _factory;

    public AgentLifecycleTests(Support360ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EnrollmentHeartbeatTelemetryAndRevocationWorkEndToEnd()
    {
        using var userClient = _factory.CreateClient();
        var token = await LoginAsync(userClient, Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var anonClient = _factory.CreateClient();
        var enrollment = await CreateEnrollmentAsync(userClient, allowDeploymentExecution: true);
        Assert.True(enrollment.AllowDeploymentExecution);
        var agent = await EnrollAsync(anonClient, enrollment.Token, "e2e-linux-01", "Linux", ["metrics", "services", "deployment_execution"]);

        Assert.True(agent.AgentId != Guid.Empty);
        Assert.StartsWith(agent.ApiKeyPrefix, agent.ApiKey);
        Assert.Equal(8, agent.AllowedMetrics.Count);
        Assert.True(agent.HeartbeatIntervalSeconds > 0);

        var replay = await anonClient.PostAsJsonAsync("/api/agent/v1/enroll", new EnrollAgentRequest(
            enrollment.Token, "replay-host", "Linux", null, null, null, null));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        using var agentClient = _factory.CreateClient();
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);

        var heartbeat = await agentClient.PostAsJsonAsync("/api/agent/v1/heartbeat", new HeartbeatRequest(
            "0.1.0",
            "Ubuntu 24.04",
            "amd64",
            1234,
            true,
            "ok",
            ["metrics", "services", "deployment_execution"],
            [new ServiceStatusDto("nginx", "systemd", "Running", DateTimeOffset.UtcNow, "active")]));
        heartbeat.EnsureSuccessStatusCode();
        var heartbeatResponse = await heartbeat.Content.ReadFromJsonAsync<HeartbeatResponse>();
        Assert.NotNull(heartbeatResponse);
        Assert.Contains("cpu.usage.percent", heartbeatResponse!.AllowedMetrics);

        var now = DateTimeOffset.UtcNow;
        var telemetry = await agentClient.PostAsJsonAsync("/api/agent/v1/telemetry", new TelemetryBatchRequest(
        [
            new MetricSampleDto(Guid.NewGuid().ToString(), "cpu.usage.percent", 42.5, "percent", now),
            new MetricSampleDto(Guid.NewGuid().ToString(), "memory.used.percent", 61.2, "percent", now),
            new MetricSampleDto(Guid.NewGuid().ToString(), "disk.used.percent", 73.1, "percent", now)
        ]));
        telemetry.EnsureSuccessStatusCode();
        var telemetryResponse = await telemetry.Content.ReadFromJsonAsync<TelemetryIngestResponse>();
        Assert.NotNull(telemetryResponse);
        Assert.Equal(3, telemetryResponse!.Accepted);
        Assert.Equal(0, telemetryResponse.Rejected);

        var duplicate = await agentClient.PostAsJsonAsync("/api/agent/v1/telemetry", new TelemetryBatchRequest(
        [
            new MetricSampleDto("duplicate-event", "cpu.usage.percent", 1, "percent", now),
            new MetricSampleDto("duplicate-event", "cpu.usage.percent", 2, "percent", now)
        ]));
        duplicate.EnsureSuccessStatusCode();
        var duplicateResponse = await duplicate.Content.ReadFromJsonAsync<TelemetryIngestResponse>();
        Assert.NotNull(duplicateResponse);
        Assert.Equal(1, duplicateResponse!.Accepted);
        Assert.Equal(1, duplicateResponse.Duplicates);

        var invalidUnit = await agentClient.PostAsJsonAsync("/api/agent/v1/telemetry", new TelemetryBatchRequest(
        [
            new MetricSampleDto(Guid.NewGuid().ToString(), "cpu.usage.percent", 10, "bytes", now)
        ]));
        invalidUnit.EnsureSuccessStatusCode();
        var invalidResponse = await invalidUnit.Content.ReadFromJsonAsync<TelemetryIngestResponse>();
        Assert.NotNull(invalidResponse);
        Assert.Equal(1, invalidResponse!.Rejected);

        var agents = await userClient.GetFromJsonAsync<List<AgentSummaryDto>>("/api/agents");
        Assert.NotNull(agents);
        var summary = Assert.Single(agents!, a => a.Id == agent.AgentId);
        Assert.Equal("e2e-linux-01", summary.Hostname);
        Assert.Equal(42.5, summary.CpuPercent);
        Assert.Equal(0, summary.ActiveAlerts);

        var detail = await userClient.GetFromJsonAsync<AgentDetailResponse>($"/api/agents/{agent.AgentId}");
        Assert.NotNull(detail);
        Assert.Single(detail!.Services);
        Assert.Contains("metrics", detail.Capabilities);
        Assert.Contains("deployment_execution", detail.Capabilities);
        Assert.True(detail.DeploymentExecutionAllowed);

        var metrics = await userClient.GetFromJsonAsync<AgentMetricsResponse>($"/api/agents/{agent.AgentId}/metrics?range=15m");
        Assert.NotNull(metrics);
        Assert.Contains("cpu.usage.percent", metrics!.Latest.Keys);
        Assert.Equal(42.5, metrics.Latest["cpu.usage.percent"]);

        var overview = await userClient.GetFromJsonAsync<OverviewDto>("/api/overview");
        Assert.NotNull(overview);
        Assert.True(overview!.TotalAgents >= 1);
        Assert.True(overview.OnlineAgents >= 1);

        var revoke = await userClient.PostAsync($"/api/agents/{agent.AgentId}/revoke", null);
        revoke.EnsureSuccessStatusCode();
        var revoked = await revoke.Content.ReadFromJsonAsync<RevokeAgentResponse>();
        Assert.NotNull(revoked);
        Assert.Equal("Revoked", revoked!.Status);

        var afterRevoke = await agentClient.PostAsJsonAsync("/api/agent/v1/heartbeat", new HeartbeatRequest(
            null, null, null, null, true, "ok", null, null));
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
    }

    [Fact]
    public async Task TenantScopedQueriesNeverExposeAnotherTenantsAgent()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenantId = Guid.NewGuid();
        const string email = "ops-tenant-b@support360.local";
        const string password = "Tenant-B-Password-123!";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            tenantContext.SetSystem();

            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = $"Tenant B {suffix}",
                Slug = $"tenant-b-{suffix}"
            });
            db.Users.Add(new User
            {
                TenantId = tenantId,
                Email = email,
                EmailNormalized = email,
                DisplayName = "Tenant B Operator",
                PasswordHash = hasher.Hash(password),
                Role = UserRole.TenantAdmin
            });
            db.Agents.Add(new Agent
            {
                TenantId = tenantId,
                Hostname = $"tenant-b-host-{suffix}",
                Platform = AgentPlatform.Linux,
                Status = AgentStatus.Active,
                Connectivity = AgentConnectivity.Online,
                LastHeartbeatAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var token = await LoginAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var agents = await client.GetFromJsonAsync<List<AgentSummaryDto>>("/api/agents");
        Assert.NotNull(agents);
        Assert.NotEmpty(agents!);
        Assert.All(agents, a => Assert.Contains("tenant-b-host-", a.Hostname));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetTenant(tenantId);

            var visible = await db.Agents.AsNoTracking().ToListAsync();
            Assert.NotEmpty(visible);
            Assert.All(visible, a => Assert.Equal(tenantId, a.TenantId));
        }
    }

    [Fact]
    public async Task ApplicationCheckConfigurationAndResultHistoryAreTenantBoundAndIdempotent()
    {
        using var userClient = _factory.CreateClient();
        var token = await LoginAsync(userClient, Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var agentClient = _factory.CreateClient();

        var enrollment = await CreateEnrollmentAsync(userClient);
        var enrolled = await EnrollAsync(agentClient, enrollment.Token, "app-check-host", "Linux");
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", enrolled.ApiKey);

        var create = await userClient.PostAsJsonAsync("/api/application-checks", new CreateApplicationCheckRequest(
            enrolled.AgentId, "API health", "Http", "https://service.example/health", 60));
        create.EnsureSuccessStatusCode();
        var check = await create.Content.ReadFromJsonAsync<ApplicationCheckDto>();
        Assert.NotNull(check);
        Assert.Equal("Unknown", check!.LastStatus);

        var config = await agentClient.GetFromJsonAsync<List<AgentApplicationCheckDto>>("/api/agent/v1/application-checks");
        Assert.NotNull(config);
        Assert.Contains(config!, item => item.Id == check.Id && item.Target == "https://service.example/health");

        var now = DateTimeOffset.UtcNow;
        var result = new ApplicationCheckResultDto(check.Id, "check-result-1", "Healthy", now.AddSeconds(-1), now, 14, null, null);
        var ingest = await agentClient.PostAsJsonAsync("/api/agent/v1/application-checks/results", new ApplicationCheckResultsRequest([result]));
        ingest.EnsureSuccessStatusCode();
        var accepted = await ingest.Content.ReadFromJsonAsync<ApplicationCheckResultsResponse>();
        Assert.Equal(1, accepted!.Accepted);

        var duplicate = await agentClient.PostAsJsonAsync("/api/agent/v1/application-checks/results", new ApplicationCheckResultsRequest([result]));
        duplicate.EnsureSuccessStatusCode();
        var duplicateResponse = await duplicate.Content.ReadFromJsonAsync<ApplicationCheckResultsResponse>();
        Assert.Equal(1, duplicateResponse!.Duplicates);

        var history = await userClient.GetFromJsonAsync<List<ApplicationCheckHistoryItemDto>>($"/api/application-checks/{check.Id}/history");
        Assert.NotNull(history);
        var item = Assert.Single(history!);
        Assert.Equal("check-result-1", item.EventId);
        Assert.Equal("Healthy", item.Status);
        Assert.Equal(14, item.DurationMilliseconds);

        var processCreate = await userClient.PostAsJsonAsync("/api/application-checks", new CreateApplicationCheckRequest(
            enrolled.AgentId, "Worker process", "Process", "worker", 60));
        processCreate.EnsureSuccessStatusCode();
        var processCheck = await processCreate.Content.ReadFromJsonAsync<ApplicationCheckDto>();
        Assert.NotNull(processCheck);
        var processResult = new ApplicationCheckResultDto(processCheck!.Id, "process-result-invalid", "Healthy", now.AddSeconds(-1), now, 1, null, null);
        var forbiddenClaim = await agentClient.PostAsJsonAsync("/api/agent/v1/application-checks/results", new ApplicationCheckResultsRequest([processResult]));
        forbiddenClaim.EnsureSuccessStatusCode();
        var rejectedClaim = await forbiddenClaim.Content.ReadFromJsonAsync<ApplicationCheckResultsResponse>();
        Assert.Equal(1, rejectedClaim!.Rejected);

        var observedResult = processResult with { EventId = "process-result-present", Status = "Present" };
        var observed = await agentClient.PostAsJsonAsync("/api/agent/v1/application-checks/results", new ApplicationCheckResultsRequest([observedResult]));
        observed.EnsureSuccessStatusCode();
        var observedResponse = await observed.Content.ReadFromJsonAsync<ApplicationCheckResultsResponse>();
        Assert.Equal(1, observedResponse!.Accepted);
        var processHistory = await userClient.GetFromJsonAsync<List<ApplicationCheckHistoryItemDto>>($"/api/application-checks/{processCheck.Id}/history");
        Assert.Equal("Present", Assert.Single(processHistory!).Status);
    }

    private async Task<EnrollmentCreatedResponse> CreateEnrollmentAsync(HttpClient client, bool allowDeploymentExecution = false)
    {
        var response = await client.PostAsJsonAsync("/api/enrollments", new CreateEnrollmentRequest("Linux", allowDeploymentExecution));
        response.EnsureSuccessStatusCode();
        var enrollment = await response.Content.ReadFromJsonAsync<EnrollmentCreatedResponse>();
        Assert.NotNull(enrollment);
        return enrollment!;
    }

    private static async Task<EnrollAgentResponse> EnrollAsync(
        HttpClient client,
        string enrollmentToken,
        string hostname,
        string platform,
        IReadOnlyList<string>? capabilities = null)
    {
        var response = await client.PostAsJsonAsync("/api/agent/v1/enroll", new EnrollAgentRequest(
            enrollmentToken,
            hostname,
            platform,
            "Ubuntu 24.04",
            "amd64",
            "0.1.0",
            capabilities ?? ["metrics", "services"]));
        response.EnsureSuccessStatusCode();
        var agent = await response.Content.ReadFromJsonAsync<EnrollAgentResponse>();
        Assert.NotNull(agent);
        return agent!;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        return login!.AccessToken;
    }
}
