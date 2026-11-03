using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Support360.Application.Abstractions;
using Support360.Contracts.Agents;
using Support360.Contracts.Auth;
using Support360.Contracts.Deployments;
using Support360.Contracts.Operations;
using Support360.Domain;
using Support360.Domain.Entities;
using Support360.Infrastructure.Persistence;

namespace Support360.Tests.Integration;

public sealed class DeploymentRequestTests : IClassFixture<Support360ApiFactory>
{
    private readonly Support360ApiFactory _factory;
    public DeploymentRequestTests(Support360ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task DeploymentRequestBindsPackageTargetAndApprovalState()
    {
        using var admin = _factory.CreateClient();
        var adminLogin = await LoginAsync(admin, Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);

        var enrollmentResponse = await admin.PostAsJsonAsync("/api/enrollments", new CreateEnrollmentRequest("Linux"));
        enrollmentResponse.EnsureSuccessStatusCode();
        var enrollment = (await enrollmentResponse.Content.ReadFromJsonAsync<EnrollmentCreatedResponse>())!;
        using var agentClient = _factory.CreateClient();
        var enrollResponse = await agentClient.PostAsJsonAsync("/api/agent/v1/enroll", new EnrollAgentRequest(
            enrollment.Token, $"deploy-{Guid.NewGuid():N}"[..20], "Linux", "Ubuntu 24.04", "amd64", "0.1.0", ["metrics"]));
        enrollResponse.EnsureSuccessStatusCode();
        var agent = (await enrollResponse.Content.ReadFromJsonAsync<EnrollAgentResponse>())!;

        var packageUpload = await UploadZipAsync(admin, $"package-{Guid.NewGuid():N}"[..18], "1.0.0");
        Assert.Equal(HttpStatusCode.Created, packageUpload.StatusCode);
        var package = (await packageUpload.Content.ReadFromJsonAsync<DeploymentPackageDto>())!;
        var now = DateTimeOffset.UtcNow;
        var changeResponse = await admin.PostAsJsonAsync("/api/change-requests", new CreateChangeRequest(
            "Deploy approved package", "Install the package using the approved deployment policy.", "Low", "test",
            now.AddHours(1), now.AddHours(2), null));
        changeResponse.EnsureSuccessStatusCode();
        var change = (await changeResponse.Content.ReadFromJsonAsync<ChangeRequestDto>())!;

        var requestResponse = await admin.PostAsJsonAsync("/api/deployments", new CreateDeploymentRequest(agent.AgentId, package.Id, change.Id, null, null, null));
        Assert.True(requestResponse.StatusCode == HttpStatusCode.Created,
            $"Expected deployment request creation, received {(int)requestResponse.StatusCode}: {await requestResponse.Content.ReadAsStringAsync()}");
        var deployment = (await requestResponse.Content.ReadFromJsonAsync<DeploymentRequestDto>())!;
        Assert.Equal("PendingApproval", deployment.Status);
        Assert.Equal(agent.AgentId, deployment.TargetAgentId);
        Assert.Equal("1.0.0", deployment.PackageVersion);
        Assert.Equal(package.Sha256, deployment.PackageSha256);
        Assert.Equal("test", deployment.Environment);
        Assert.Equal(0, deployment.ReceivedApprovals);

        var duplicate = await admin.PostAsJsonAsync("/api/deployments", new CreateDeploymentRequest(agent.AgentId, package.Id, change.Id, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var approver = await CreateApproverAsync(adminLogin.User.TenantId);
        using var approverClient = _factory.CreateClient();
        var approverLogin = await LoginAsync(approverClient, approver.Email, approver.Password);
        approverClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", approverLogin.AccessToken);
        var approval = await approverClient.PostAsJsonAsync($"/api/change-requests/{change.Id}/decision", new DecideChangeRequest("Approved", "Reviewed deployment change."));
        approval.EnsureSuccessStatusCode();

        var current = await admin.GetFromJsonAsync<DeploymentRequestDto>($"/api/deployments/{deployment.Id}");
        Assert.Equal("Approved", current!.Status);
        Assert.Equal(1, current.ReceivedApprovals);
        var list = await admin.GetFromJsonAsync<List<DeploymentRequestDto>>("/api/deployments");
        Assert.Contains(list!, x => x.Id == deployment.Id && x.PackageSha256 == package.Sha256);

        var otherTenantChangeId = Guid.NewGuid();
        var foreignChange = await admin.PostAsJsonAsync("/api/deployments", new CreateDeploymentRequest(agent.AgentId, package.Id, otherTenantChangeId, null, null, null));
        Assert.Equal(HttpStatusCode.NotFound, foreignChange.StatusCode);
    }

    private async Task<(string Email, string Password)> CreateApproverAsync(Guid tenantId)
    {
        var email = $"deployment-approver-{Guid.NewGuid():N}@support360.local";
        const string password = "Deployment-Approver-Password-123!";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        tenant.SetSystem();
        db.Users.Add(new User
        {
            TenantId = tenantId, Email = email, EmailNormalized = email.ToLowerInvariant(),
            DisplayName = "Deployment Approver", PasswordHash = hasher.Hash(password), Role = UserRole.Approver
        });
        await db.SaveChangesAsync();
        return (email, password);
    }

    private static async Task<HttpResponseMessage> UploadZipAsync(HttpClient client, string name, string version)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("app.txt").Open()))
            writer.Write("safe test package");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent(version), "version");
        var file = new ByteArrayContent(bytes.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", $"{name}.zip");
        return await client.PostAsync("/api/deployment-packages", form);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
