using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Support360.Application.Abstractions;
using Support360.Contracts.Auth;
using Support360.Contracts.Operations;
using Support360.Domain;
using Support360.Domain.Entities;
using Support360.Infrastructure.Persistence;

namespace Support360.Tests.Integration;

public sealed class IncidentChangeTests : IClassFixture<Support360ApiFactory>
{
    private readonly Support360ApiFactory _factory;
    public IncidentChangeTests(Support360ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task IncidentLifecycleAndChangeApprovalEnforceTenantPolicyAndSeparationOfDuties()
    {
        using var adminClient = _factory.CreateClient();
        var adminLogin = await LoginAsync(adminClient, Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);
        var tenantId = adminLogin.User.TenantId;

        var policy = await adminClient.PutAsJsonAsync("/api/change-management/policy", new UpdateChangeApprovalPolicyRequest(2, true));
        policy.EnsureSuccessStatusCode();

        var createIncident = await adminClient.PostAsJsonAsync("/api/incidents", new CreateIncidentRequest(
            "Database latency", "Database response time increased for the primary tenant.", "High", [], null));
        createIncident.EnsureSuccessStatusCode();
        var incident = (await createIncident.Content.ReadFromJsonAsync<IncidentDto>())!;
        Assert.StartsWith("INC-", incident.Number);
        Assert.Equal("New", incident.Status);

        var transition = await adminClient.PutAsJsonAsync($"/api/incidents/{incident.Id}/status", new TransitionIncidentRequest("Acknowledged", null));
        transition.EnsureSuccessStatusCode();
        Assert.Equal("Acknowledged", (await transition.Content.ReadFromJsonAsync<IncidentDto>())!.Status);
        var invalidTransition = await adminClient.PutAsJsonAsync($"/api/incidents/{incident.Id}/status", new TransitionIncidentRequest("Closed", null));
        Assert.Equal(HttpStatusCode.Conflict, invalidTransition.StatusCode);

        var createChange = await adminClient.PostAsJsonAsync("/api/change-requests", new CreateChangeRequest(
            "Database index maintenance", "Apply the approved index maintenance plan.", "Medium risk; monitor write latency.",
            "production", DateTimeOffset.UtcNow.AddHours(2), DateTimeOffset.UtcNow.AddHours(3), incident.Id));
        createChange.EnsureSuccessStatusCode();
        var change = (await createChange.Content.ReadFromJsonAsync<ChangeRequestDto>())!;
        Assert.Equal(2, change.RequiredApprovals);
        Assert.Equal("PendingApproval", change.Status);

        var adminSelfApproval = await adminClient.PostAsJsonAsync($"/api/change-requests/{change.Id}/decision", new DecideChangeRequest("Approved", "I requested this."));
        Assert.Equal(HttpStatusCode.Forbidden, adminSelfApproval.StatusCode);

        var approvers = await CreateApproversAsync(tenantId);
        using var firstApprover = _factory.CreateClient();
        var firstLogin = await LoginAsync(firstApprover, approvers[0].Email, approvers[0].Password);
        firstApprover.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", firstLogin.AccessToken);
        var firstDecision = await firstApprover.PostAsJsonAsync($"/api/change-requests/{change.Id}/decision", new DecideChangeRequest("Approved", "Reviewed risk."));
        firstDecision.EnsureSuccessStatusCode();
        Assert.Equal("PendingApproval", (await firstDecision.Content.ReadFromJsonAsync<ChangeRequestDto>())!.Status);
        var duplicateDecision = await firstApprover.PostAsJsonAsync($"/api/change-requests/{change.Id}/decision", new DecideChangeRequest("Approved", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicateDecision.StatusCode);

        using var secondApprover = _factory.CreateClient();
        var secondLogin = await LoginAsync(secondApprover, approvers[1].Email, approvers[1].Password);
        secondApprover.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secondLogin.AccessToken);
        var secondDecision = await secondApprover.PostAsJsonAsync($"/api/change-requests/{change.Id}/decision", new DecideChangeRequest("Approved", "Approved."));
        secondDecision.EnsureSuccessStatusCode();
        var approved = (await secondDecision.Content.ReadFromJsonAsync<ChangeRequestDto>())!;
        Assert.Equal("Approved", approved.Status);
        Assert.Equal(2, approved.Approvals.Count);
        Assert.Equal(incident.Id, approved.RelatedIncidentId);

        var incidents = await adminClient.GetFromJsonAsync<List<IncidentDto>>("/api/incidents?status=acknowledged");
        Assert.Contains(incidents!, item => item.Id == incident.Id && item.RelatedChangeRequestIds.Contains(change.Id));
        var changes = await adminClient.GetFromJsonAsync<List<ChangeRequestDto>>("/api/change-requests");
        Assert.Contains(changes!, item => item.Id == change.Id && item.Status == "Approved");

        var cancelCreate = await adminClient.PostAsJsonAsync("/api/change-requests", new CreateChangeRequest(
            "Cancel test change", "Change opened to verify cancellation.", "Low risk.", "test",
            DateTimeOffset.UtcNow.AddHours(4), DateTimeOffset.UtcNow.AddHours(5), incident.Id));
        cancelCreate.EnsureSuccessStatusCode();
        var cancelRequest = (await cancelCreate.Content.ReadFromJsonAsync<ChangeRequestDto>())!;
        var cancelResponse = await adminClient.PostAsync($"/api/change-requests/{cancelRequest.Id}/cancel", null);
        cancelResponse.EnsureSuccessStatusCode();
        Assert.Equal("Cancelled", (await cancelResponse.Content.ReadFromJsonAsync<ChangeRequestDto>())!.Status);

        using var otherTenant = await CreateOtherTenantClientAsync();
        Assert.Empty(await otherTenant.GetFromJsonAsync<List<IncidentDto>>("/api/incidents") ?? []);
        Assert.Empty(await otherTenant.GetFromJsonAsync<List<ChangeRequestDto>>("/api/change-requests") ?? []);
        var crossTenantIncident = await otherTenant.PutAsJsonAsync($"/api/incidents/{incident.Id}/status", new TransitionIncidentRequest("Resolved", null));
        Assert.Equal(HttpStatusCode.NotFound, crossTenantIncident.StatusCode);
        var crossTenantChange = await otherTenant.PostAsync($"/api/change-requests/{cancelRequest.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantChange.StatusCode);
    }

    private async Task<(string Email, string Password)[]> CreateApproversAsync(Guid tenantId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var rows = new[]
        {
            (Email: $"approver-one-{suffix}@support360.local", Password: "Approver-One-Password-123!"),
            (Email: $"approver-two-{suffix}@support360.local", Password: "Approver-Two-Password-123!")
        };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        tenant.SetSystem();
        foreach (var row in rows)
            db.Users.Add(new User
            {
                TenantId = tenantId, Email = row.Email, EmailNormalized = row.Email.ToLowerInvariant(),
                DisplayName = row.Email, PasswordHash = hasher.Hash(row.Password), Role = UserRole.Approver
            });
        await db.SaveChangesAsync();
        return rows;
    }

    private async Task<HttpClient> CreateOtherTenantClientAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"incidents-other-{suffix}@support360.local";
        const string password = "Other-Tenant-Password-123!";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            tenant.SetSystem();
            var other = new Tenant { Name = $"Other Tenant {suffix}", Slug = $"other-inc-{suffix}" };
            db.Tenants.Add(other);
            db.Users.Add(new User
            {
                TenantId = other.Id, Email = email, EmailNormalized = email.ToLowerInvariant(), DisplayName = "Other admin",
                PasswordHash = hasher.Hash(password), Role = UserRole.TenantAdmin
            });
            await db.SaveChangesAsync();
        }
        var client = _factory.CreateClient();
        var login = await LoginAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
