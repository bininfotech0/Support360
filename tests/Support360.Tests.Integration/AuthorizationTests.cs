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
using Support360.Contracts.Jobs;
using Support360.Contracts.Operations;
using Support360.Contracts.Telemetry;
using Support360.Domain;
using Support360.Domain.Entities;
using Support360.Infrastructure.Persistence;

namespace Support360.Tests.Integration;

public sealed class AuthorizationTests : IClassFixture<Support360ApiFactory>
{
    private const string Password = "Authorization-Test-Password-123!";

    private readonly Support360ApiFactory _factory;

    public AuthorizationTests(Support360ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RolePoliciesEnforceLeastPrivilege()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var emails = await SeedRoleUsersAsync(suffix, UserRole.Viewer, UserRole.Operator, UserRole.Approver);

        using var admin = await CreateAuthorizedClientAsync(Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        using var viewer = await CreateAuthorizedClientAsync(emails[UserRole.Viewer], Password);
        using var operatorClient = await CreateAuthorizedClientAsync(emails[UserRole.Operator], Password);
        using var approver = await CreateAuthorizedClientAsync(emails[UserRole.Approver], Password);
        using var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/agents")).StatusCode);

        var agent = await CreateActiveAgentAsync(admin, $"rbac-agent-{suffix}");
        foreach (var client in new[] { viewer, operatorClient, approver, admin })
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/agents")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/overview")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/alert-rules")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/usage")).StatusCode);
        foreach (var client in new[] { viewer, operatorClient, approver })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/usage")).StatusCode);
        }

        var ruleRequest = new CreateAlertRuleRequest($"Rule {suffix}", "cpu.usage.percent", "GreaterThan", 95, "Warning", 1);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/alert-rules", ruleRequest)).StatusCode);
        foreach (var client in new[] { viewer, operatorClient, approver })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/alert-rules", ruleRequest)).StatusCode);
        }

        var enrollmentRequest = new CreateEnrollmentRequest("Linux");
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/enrollments", enrollmentRequest)).StatusCode);
        foreach (var client in new[] { viewer, operatorClient, approver })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/enrollments", enrollmentRequest)).StatusCode);
        }

        var missingAlertId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/alerts/{missingAlertId}/acknowledge", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await operatorClient.PostAsync($"/api/alerts/{missingAlertId}/acknowledge", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/alerts/{missingAlertId}/acknowledge", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PostAsync($"/api/alerts/{missingAlertId}/acknowledge", null)).StatusCode);

        var checkRequest = new CreateApplicationCheckRequest(agent.AgentId, $"Check {suffix}", "Http", "https://service.example/health", 60);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/application-checks", checkRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PostAsJsonAsync("/api/application-checks", checkRequest)).StatusCode);

        var operatorCheckResponse = await operatorClient.PostAsJsonAsync("/api/application-checks", checkRequest);
        Assert.Equal(HttpStatusCode.Created, operatorCheckResponse.StatusCode);
        var operatorCheck = await operatorCheckResponse.Content.ReadFromJsonAsync<ApplicationCheckDto>();
        Assert.NotNull(operatorCheck);
        var adminCheckRequest = checkRequest with { Name = $"Admin check {suffix}" };
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/application-checks", adminCheckRequest)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.DeleteAsync($"/api/application-checks/{operatorCheck!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/application-checks/{operatorCheck.Id}")).StatusCode);

        var incidentRequest = new CreateTicketRequest("Incident", $"Incident {suffix}", "Authorization matrix coverage.", null, null, "High", "High", null, null, null, null, null, null, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/tickets", incidentRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PostAsJsonAsync("/api/tickets", incidentRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await operatorClient.PostAsJsonAsync("/api/tickets", incidentRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/tickets", incidentRequest)).StatusCode);

        var now = DateTimeOffset.UtcNow;
        var changeRequest = new CreateTicketRequest(
            "Change", $"Change {suffix}", "Authorization matrix coverage.", null, null, null, null, null, null, null, "Low risk.", "production", now.AddHours(1), now.AddHours(2), null);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/tickets", changeRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PostAsJsonAsync("/api/tickets", changeRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await operatorClient.PostAsJsonAsync("/api/tickets", changeRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/tickets", changeRequest)).StatusCode);

        var missingChangeId = Guid.NewGuid();
        var decision = new DecideTicketRequest("Approved", null);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"/api/tickets/{missingChangeId}/decision", decision)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsJsonAsync($"/api/tickets/{missingChangeId}/decision", decision)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await approver.PostAsJsonAsync($"/api/tickets/{missingChangeId}/decision", decision)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/tickets/{missingChangeId}/decision", decision)).StatusCode);

        var policy = new UpdateChangeApprovalPolicyRequest(2, true);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/change-management/policy", policy)).StatusCode);
        foreach (var client in new[] { viewer, operatorClient, approver })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/change-management/policy", policy)).StatusCode);
        }
    }

    [Fact]
    public async Task CrossTenantRequestsCannotReadOrMutateAnotherTenantsResources()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var tenantBEmail = await SeedTenantAsync(suffix);

        using var adminA = await CreateAuthorizedClientAsync(Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        var agent = await CreateActiveAgentAsync(adminA, $"tenant-a-agent-{suffix}");
        var createCheck = await adminA.PostAsJsonAsync("/api/application-checks", new CreateApplicationCheckRequest(
            agent.AgentId, $"Tenant A check {suffix}", "Http", "https://service.example/health", 60));
        createCheck.EnsureSuccessStatusCode();
        var check = await createCheck.Content.ReadFromJsonAsync<ApplicationCheckDto>();
        Assert.NotNull(check);

        using var tenantB = await CreateAuthorizedClientAsync(tenantBEmail, Password);

        Assert.Equal(HttpStatusCode.NotFound, (await tenantB.GetAsync($"/api/agents/{agent.AgentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantB.GetAsync($"/api/agents/{agent.AgentId}/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantB.PostAsync($"/api/agents/{agent.AgentId}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantB.PostAsync($"/api/agents/{agent.AgentId}/rotate-credential", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await tenantB.GetAsync($"/api/application-checks/{check!.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tenantB.DeleteAsync($"/api/application-checks/{check.Id}")).StatusCode);
        var crossTenantCheck = await tenantB.PostAsJsonAsync("/api/application-checks", new CreateApplicationCheckRequest(
            agent.AgentId, "Cross-tenant check", "Http", "https://service.example/health", 60));
        Assert.Equal(HttpStatusCode.NotFound, crossTenantCheck.StatusCode);

        var checks = await tenantB.GetFromJsonAsync<List<ApplicationCheckDto>>("/api/application-checks");
        Assert.NotNull(checks);
        Assert.Empty(checks!);
        var tickets = await tenantB.GetFromJsonAsync<List<TicketDto>>("/api/tickets");
        Assert.NotNull(tickets);
        Assert.Empty(tickets!);

        using var agentClient = _factory.CreateClient();
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agent.ApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agentClient.GetAsync("/api/agents")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await adminA.GetAsync("/api/agent/v1/application-checks")).StatusCode);
    }

    [Fact]
    public async Task InactiveAccountsSuspendedTenantsAndWrongPasswordsAreRejectedAtSignIn()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var inactiveEmail = await SeedInactiveUserAsync(suffix);
        var suspendedEmail = await SeedSuspendedTenantUserAsync(suffix);
        using var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(inactiveEmail, Password))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(suspendedEmail, Password))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            Support360ApiFactory.AdminEmail, "Definitely-Wrong-Password-123!"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword))).StatusCode);
    }

    [Fact]
    public async Task HttpCheckTargetPolicyRestrictsCreateUpdateAndExistingChecks()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var adminEmail = await SeedTenantAsync(suffix);
        using var admin = await CreateAuthorizedClientAsync(adminEmail, Password);
        var agent = await CreateActiveAgentAsync(admin, $"policy-agent-{suffix}");

        var initial = await admin.GetFromJsonAsync<HttpCheckTargetPolicyDto>("/api/application-checks/target-policy");
        Assert.NotNull(initial);
        Assert.Empty(initial!.AllowedHosts);

        var policy = await admin.PutAsJsonAsync("/api/application-checks/target-policy",
            new UpdateHttpCheckTargetPolicyRequest([" Allowed.Example.com. ", "*.Internal.Example.com"]));
        policy.EnsureSuccessStatusCode();
        var mapped = await policy.Content.ReadFromJsonAsync<HttpCheckTargetPolicyDto>();
        Assert.NotNull(mapped);
        Assert.Equal(["allowed.example.com", "*.internal.example.com"], mapped!.AllowedHosts);

        var allowed = await admin.PostAsJsonAsync("/api/application-checks", new CreateApplicationCheckRequest(
            agent.AgentId, $"Allowed {suffix}", "Http", "https://allowed.example.com/health", 60));
        allowed.EnsureSuccessStatusCode();
        var allowedCheck = await allowed.Content.ReadFromJsonAsync<ApplicationCheckDto>();
        Assert.NotNull(allowedCheck);

        var wildcard = await admin.PostAsJsonAsync("/api/application-checks", new CreateApplicationCheckRequest(
            agent.AgentId, $"Wildcard {suffix}", "Http", "http://app.internal.example.com/health", 60));
        wildcard.EnsureSuccessStatusCode();

        var blocked = await admin.PostAsJsonAsync("/api/application-checks", new CreateApplicationCheckRequest(
            agent.AgentId, $"Blocked {suffix}", "Http", "https://blocked.example.net/health", 60));
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("not allowed", await blocked.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var blockedUpdate = await admin.PutAsJsonAsync($"/api/application-checks/{allowedCheck!.Id}",
            new UpdateApplicationCheckRequest($"Allowed {suffix}", "Http", "https://blocked.example.net/health", 60, true));
        Assert.Equal(HttpStatusCode.BadRequest, blockedUpdate.StatusCode);

        var conflictingPolicy = await admin.PutAsJsonAsync("/api/application-checks/target-policy",
            new UpdateHttpCheckTargetPolicyRequest(["other.example.com"]));
        Assert.Equal(HttpStatusCode.Conflict, conflictingPolicy.StatusCode);
        Assert.Contains("existing enabled HTTP checks", await conflictingPolicy.Content.ReadAsStringAsync());

        var invalidPolicy = await admin.PutAsJsonAsync("/api/application-checks/target-policy",
            new UpdateHttpCheckTargetPolicyRequest(["*"]));
        Assert.Equal(HttpStatusCode.BadRequest, invalidPolicy.StatusCode);

        var cleared = await admin.PutAsJsonAsync("/api/application-checks/target-policy",
            new UpdateHttpCheckTargetPolicyRequest([]));
        cleared.EnsureSuccessStatusCode();
        var nowAllowed = await admin.PostAsJsonAsync("/api/application-checks", new CreateApplicationCheckRequest(
            agent.AgentId, $"Now allowed {suffix}", "Http", "https://blocked.example.net/health", 60));
        nowAllowed.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task HttpCheckTargetPolicyUpdatesRequireTenantAdminButReadsAreTenantWide()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var emails = await SeedTenantRoleUsersAsync(suffix, UserRole.TenantAdmin, UserRole.Operator, UserRole.Viewer);
        using var viewer = await CreateAuthorizedClientAsync(emails[UserRole.Viewer], Password);
        using var operatorClient = await CreateAuthorizedClientAsync(emails[UserRole.Operator], Password);
        using var admin = await CreateAuthorizedClientAsync(emails[UserRole.TenantAdmin], Password);

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/application-checks/target-policy")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync("/api/application-checks/target-policy",
            new UpdateHttpCheckTargetPolicyRequest(["example.com"]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PutAsJsonAsync("/api/application-checks/target-policy",
            new UpdateHttpCheckTargetPolicyRequest(["example.com"]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/application-checks/target-policy",
            new UpdateHttpCheckTargetPolicyRequest(["example.com"]))).StatusCode);
    }

    [Fact]
    public async Task AgentCredentialsAreBoundToTheirOwnChecksTelemetryAndJobs()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        using var admin = await CreateAuthorizedClientAsync(Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        var agentA = await CreateActiveAgentAsync(admin, $"agent-a-{suffix}");
        var agentB = await CreateActiveAgentAsync(admin, $"agent-b-{suffix}");

        var checkResponse = await admin.PostAsJsonAsync("/api/application-checks", new CreateApplicationCheckRequest(
            agentB.AgentId, $"B check {suffix}", "Http", "https://service.example/health", 60));
        checkResponse.EnsureSuccessStatusCode();
        var checkB = await checkResponse.Content.ReadFromJsonAsync<ApplicationCheckDto>();
        Assert.NotNull(checkB);

        var jobKey = $"nightly-{suffix}";
        var jobResponse = await admin.PostAsJsonAsync("/api/batch-jobs", new ConfigureBatchJobRequest(
            agentB.AgentId, jobKey, $"B nightly {suffix}", "cron", "02:00 UTC"));
        jobResponse.EnsureSuccessStatusCode();

        using var agentAClient = CreateAgentClient(agentA.ApiKey);
        using var agentBClient = CreateAgentClient(agentB.ApiKey);

        var checksForA = await agentAClient.GetFromJsonAsync<List<AgentApplicationCheckDto>>("/api/agent/v1/application-checks");
        Assert.NotNull(checksForA);
        Assert.DoesNotContain(checksForA!, c => c.Id == checkB!.Id);
        var checksForB = await agentBClient.GetFromJsonAsync<List<AgentApplicationCheckDto>>("/api/agent/v1/application-checks");
        Assert.NotNull(checksForB);
        Assert.Contains(checksForB!, c => c.Id == checkB!.Id);

        var now = DateTimeOffset.UtcNow;
        var crossAgentResult = await agentAClient.PostAsJsonAsync("/api/agent/v1/application-checks/results",
            new ApplicationCheckResultsRequest([
                new ApplicationCheckResultDto(checkB!.Id, $"cross-{suffix}", "Healthy", now.AddSeconds(-1), now, 1, null, null)
            ]));
        crossAgentResult.EnsureSuccessStatusCode();
        var crossResponse = await crossAgentResult.Content.ReadFromJsonAsync<ApplicationCheckResultsResponse>();
        Assert.NotNull(crossResponse);
        Assert.Equal(1, crossResponse!.Rejected);
        var historyB = await admin.GetFromJsonAsync<List<ApplicationCheckHistoryItemDto>>($"/api/application-checks/{checkB.Id}/history");
        Assert.NotNull(historyB);
        Assert.Empty(historyB!);

        var sharedEvent = $"shared-{suffix}";
        foreach (var client in new[] { agentAClient, agentBClient })
        {
            var telemetry = await client.PostAsJsonAsync("/api/agent/v1/telemetry", new TelemetryBatchRequest(
                [new MetricSampleDto(sharedEvent, "cpu.usage.percent", 10, "percent", now)]));
            telemetry.EnsureSuccessStatusCode();
            var ingest = await telemetry.Content.ReadFromJsonAsync<TelemetryIngestResponse>();
            Assert.NotNull(ingest);
            Assert.Equal(1, ingest!.Accepted);
        }

        var correlationId = $"corr-{suffix}";
        var jobIngest = await agentAClient.PostAsJsonAsync("/api/agent/v1/job-events", new JobEventBatchRequest([
            new JobEventDto(correlationId, jobKey, $"A nightly {suffix}", "cron", now.AddMinutes(-1), now, "Failed", 1, "exit 1")
        ]));
        jobIngest.EnsureSuccessStatusCode();
        var jobIngestResponse = await jobIngest.Content.ReadFromJsonAsync<JobEventsResponse>();
        Assert.NotNull(jobIngestResponse);
        Assert.Equal(1, jobIngestResponse!.Accepted);

        var jobs = await admin.GetFromJsonAsync<List<BatchJobDto>>("/api/batch-jobs");
        Assert.NotNull(jobs);
        var jobB = Assert.Single(jobs!, j => j.AgentId == agentB.AgentId && j.JobKey == jobKey);
        Assert.Equal($"B nightly {suffix}", jobB.Name);
        Assert.DoesNotContain(jobB.RecentExecutions, e => e.CorrelationId == correlationId);
        var jobA = Assert.Single(jobs!, j => j.AgentId == agentA.AgentId && j.JobKey == jobKey);
        Assert.Equal($"A nightly {suffix}", jobA.Name);
        Assert.Contains(jobA.RecentExecutions, e => e.CorrelationId == correlationId);
    }

    [Fact]
    public async Task RotatedCredentialsAndSuspendedTenantAgentsAreRejected()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        using var admin = await CreateAuthorizedClientAsync(Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        var agent = await CreateActiveAgentAsync(admin, $"rotate-agent-{suffix}");
        using var originalCredentialClient = CreateAgentClient(agent.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await originalCredentialClient.GetAsync("/api/agent/v1/application-checks")).StatusCode);

        var rotate = await admin.PostAsync($"/api/agents/{agent.AgentId}/rotate-credential", null);
        rotate.EnsureSuccessStatusCode();
        var rotated = await rotate.Content.ReadFromJsonAsync<RotateCredentialResponse>();
        Assert.NotNull(rotated);

        Assert.Equal(HttpStatusCode.Unauthorized, (await originalCredentialClient.GetAsync("/api/agent/v1/application-checks")).StatusCode);
        using var rotatedClient = CreateAgentClient(rotated!.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await rotatedClient.GetAsync("/api/agent/v1/application-checks")).StatusCode);

        var tenantEmail = await SeedTenantAsync(suffix);
        using var tenantAdmin = await CreateAuthorizedClientAsync(tenantEmail, Password);
        var tenantAgent = await CreateActiveAgentAsync(tenantAdmin, $"suspended-agent-{suffix}");
        using var tenantAgentClient = CreateAgentClient(tenantAgent.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await tenantAgentClient.GetAsync("/api/agent/v1/application-checks")).StatusCode);

        await SetTenantStatusAsync(tenantEmail, TenantStatus.Suspended);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tenantAgentClient.GetAsync("/api/agent/v1/application-checks")).StatusCode);
    }

    private HttpClient CreateAgentClient(string apiKey)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private async Task SetTenantStatusAsync(string userEmail, TenantStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetSystem();
        var normalized = userEmail.Trim().ToLowerInvariant();
        var tenantId = await db.Users.IgnoreQueryFilters()
            .Where(u => u.EmailNormalized == normalized)
            .Select(u => u.TenantId)
            .SingleAsync();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == tenantId);
        tenant.Status = status;
        await db.SaveChangesAsync();
    }

    private async Task<Dictionary<UserRole, string>> SeedRoleUsersAsync(string suffix, params UserRole[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        tenantContext.SetSystem();
        var tenantId = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Slug == "integration")
            .Select(t => t.Id)
            .SingleAsync();

        var emails = new Dictionary<UserRole, string>();
        foreach (var role in roles)
        {
            var email = $"{role.ToString().ToLowerInvariant()}-{suffix}@support360.local";
            db.Users.Add(new User
            {
                TenantId = tenantId,
                Email = email,
                EmailNormalized = email,
                DisplayName = $"{role} {suffix}",
                PasswordHash = hasher.Hash(Password),
                Role = role
            });
            emails[role] = email;
        }

        await db.SaveChangesAsync();
        return emails;
    }

    private async Task<Dictionary<UserRole, string>> SeedTenantRoleUsersAsync(string suffix, params UserRole[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        tenantContext.SetSystem();

        var tenant = new Tenant
        {
            Name = $"Policy Tenant {suffix}",
            Slug = $"policy-{suffix}-{Guid.NewGuid():N}"[..40]
        };
        db.Tenants.Add(tenant);

        var emails = new Dictionary<UserRole, string>();
        foreach (var role in roles)
        {
            var email = $"{role.ToString().ToLowerInvariant()}-{suffix}@support360.local";
            db.Users.Add(new User
            {
                TenantId = tenant.Id,
                Email = email,
                EmailNormalized = email,
                DisplayName = $"{role} {suffix}",
                PasswordHash = hasher.Hash(Password),
                Role = role
            });
            emails[role] = email;
        }

        await db.SaveChangesAsync();
        return emails;
    }

    private async Task<string> SeedInactiveUserAsync(string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        tenantContext.SetSystem();
        var tenantId = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Slug == "integration")
            .Select(t => t.Id)
            .SingleAsync();
        var email = $"inactive-{suffix}@support360.local";
        db.Users.Add(new User
        {
            TenantId = tenantId,
            Email = email,
            EmailNormalized = email,
            DisplayName = "Inactive User",
            PasswordHash = hasher.Hash(Password),
            Role = UserRole.Operator,
            IsActive = false
        });
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<string> SeedTenantAsync(string suffix)
    {
        var email = $"tenant-b-{suffix}@support360.local";
        await SeedTenantWithAdminAsync(email, TenantStatus.Active, suffix);
        return email;
    }

    private async Task<string> SeedSuspendedTenantUserAsync(string suffix)
    {
        var email = $"suspended-{suffix}@support360.local";
        await SeedTenantWithAdminAsync(email, TenantStatus.Suspended, suffix);
        return email;
    }

    private async Task SeedTenantWithAdminAsync(string email, TenantStatus status, string suffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        tenantContext.SetSystem();
        var tenant = new Tenant
        {
            Name = $"Tenant {suffix}",
            Slug = $"tenant-{suffix}-{Guid.NewGuid():N}"[..40],
            Status = status
        };
        db.Tenants.Add(tenant);
        db.Users.Add(new User
        {
            TenantId = tenant.Id,
            Email = email,
            EmailNormalized = email,
            DisplayName = "Tenant Admin",
            PasswordHash = hasher.Hash(Password),
            Role = UserRole.TenantAdmin
        });
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
        return client;
    }

    private async Task<EnrollAgentResponse> CreateActiveAgentAsync(HttpClient adminClient, string hostname)
    {
        var response = await adminClient.PostAsJsonAsync("/api/enrollments", new CreateEnrollmentRequest("Linux"));
        response.EnsureSuccessStatusCode();
        var enrollment = await response.Content.ReadFromJsonAsync<EnrollmentCreatedResponse>();
        Assert.NotNull(enrollment);

        using var agentClient = _factory.CreateClient();
        var enrollResponse = await agentClient.PostAsJsonAsync("/api/agent/v1/enroll", new EnrollAgentRequest(
            enrollment!.Token, hostname, "Linux", "Ubuntu 24.04", "amd64", "0.1.0", ["metrics", "services"]));
        enrollResponse.EnsureSuccessStatusCode();
        var agent = await enrollResponse.Content.ReadFromJsonAsync<EnrollAgentResponse>();
        Assert.NotNull(agent);
        return agent!;
    }
}
