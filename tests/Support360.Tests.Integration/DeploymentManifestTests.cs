using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Support360.Application.Abstractions;
using Support360.Contracts.Agents;
using Support360.Contracts.Auth;
using Support360.Contracts.Deployments;
using Support360.Contracts.Operations;
using Support360.Domain;
using Support360.Domain.Entities;
using Support360.Infrastructure.Persistence;
using Support360.Infrastructure.Services;

namespace Support360.Tests.Integration;

public sealed class DeploymentManifestTests : IClassFixture<Support360ApiFactory>
{
    private readonly Support360ApiFactory _factory;
    public DeploymentManifestTests(Support360ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task SignedManifestArtifactAndResultAreBoundToTheTargetAgent()
    {
        using var admin = _factory.CreateClient();
        var adminLogin = await LoginAsync(admin, Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);

        var agent = await EnrollAsync(admin, allowDeployments: true);
        using var agentClient = AgentClient(agent.ApiKey);
        var otherAgent = await EnrollAsync(admin, allowDeployments: true);
        using var otherAgentClient = AgentClient(otherAgent.ApiKey);

        var packageBytes = CreateZip();
        var packageName = $"manifest-{Guid.NewGuid():N}"[..20];
        var package = await UploadPackageAsync(admin, packageName, "1.0.0", packageBytes);
        var expectedDigest = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        Assert.Equal(expectedDigest, package.Sha256);

        var now = DateTimeOffset.UtcNow;
        var changeResponse = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Change", "Deploy signed package", "Deploy through the allowlisted agent executor.",
            null, null, null, null, null, null, null, "Low risk.", "production",
            now.AddHours(1), now.AddHours(2), null));
        changeResponse.EnsureSuccessStatusCode();
        var change = (await changeResponse.Content.ReadFromJsonAsync<TicketDto>())!;

        var targetPath = $"/srv/support360/{packageName}";
        var requestResponse = await admin.PostAsJsonAsync("/api/deployments",
            new CreateDeploymentRequest(agent.AgentId, package.Id, change.Id, null, null, targetPath));
        requestResponse.EnsureSuccessStatusCode();
        var deployment = (await requestResponse.Content.ReadFromJsonAsync<DeploymentRequestDto>())!;

        var approver = await CreateApproverAsync(adminLogin.User.TenantId);
        using var approverClient = _factory.CreateClient();
        var approverLogin = await LoginAsync(approverClient, approver.Email, approver.Password);
        approverClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", approverLogin.AccessToken);
        var approval = await approverClient.PostAsJsonAsync($"/api/tickets/{change.Id}/decision",
            new DecideTicketRequest("Approved", "Approved for the signed deployment test."));
        approval.EnsureSuccessStatusCode();

        var approved = await admin.GetFromJsonAsync<DeploymentRequestDto>($"/api/deployments/{deployment.Id}");
        Assert.Equal("Approved", approved!.Status);

        var pending = await agentClient.GetFromJsonAsync<List<DeploymentRequestDto>>("/api/agent/v1/deployments/pending");
        Assert.Contains(pending!, item => item.Id == deployment.Id);

        var manifestResponse = await agentClient.GetAsync($"/api/agent/v1/deployments/{deployment.Id}/manifest");
        Assert.Equal(HttpStatusCode.OK, manifestResponse.StatusCode);
        var manifest = (await manifestResponse.Content.ReadFromJsonAsync<DeploymentManifestDto>())!;
        Assert.Equal(deployment.Id, manifest.DeploymentId);
        Assert.Equal(agent.AgentId, manifest.AgentId);
        Assert.Equal(agent.TenantId, manifest.TenantId);
        Assert.Equal(targetPath, manifest.TargetPath);
        Assert.Equal(expectedDigest, manifest.PackageSha256);
        Assert.True(manifest.ExpiresAt > manifest.IssuedAt);
        Assert.True(VerifyManifestSignature(manifest));

        var repeated = await agentClient.GetFromJsonAsync<DeploymentManifestDto>($"/api/agent/v1/deployments/{deployment.Id}/manifest");
        Assert.Equal(manifest.Nonce, repeated!.Nonce);

        var artifactResponse = await agentClient.GetAsync($"/api/agent/v1/deployments/{deployment.Id}/artifact");
        Assert.Equal(HttpStatusCode.OK, artifactResponse.StatusCode);
        var artifact = await artifactResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(expectedDigest, Convert.ToHexString(SHA256.HashData(artifact)).ToLowerInvariant());

        var wrongAgentArtifact = await otherAgentClient.GetAsync($"/api/agent/v1/deployments/{deployment.Id}/artifact");
        Assert.Equal(HttpStatusCode.NotFound, wrongAgentArtifact.StatusCode);
        var wrongAgentResult = await otherAgentClient.PostAsJsonAsync("/api/agent/v1/deployments/result",
            new { deploymentId = deployment.Id, status = "succeeded", detail = "spoofed" });
        Assert.Equal(HttpStatusCode.NotFound, wrongAgentResult.StatusCode);

        var result = await agentClient.PostAsJsonAsync("/api/agent/v1/deployments/result",
            new { deploymentId = deployment.Id, status = "succeeded", detail = "artifact deployed" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);

        var completed = await admin.GetFromJsonAsync<DeploymentRequestDto>($"/api/deployments/{deployment.Id}");
        Assert.Equal("Succeeded", completed!.Status);

        var replay = await agentClient.GetAsync($"/api/agent/v1/deployments/{deployment.Id}/manifest");
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        var duplicateResult = await agentClient.PostAsJsonAsync("/api/agent/v1/deployments/result",
            new { deploymentId = deployment.Id, status = "failed", detail = "replay" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateResult.StatusCode);
    }

    [Fact]
    public async Task AgentWithoutDeploymentGrantCannotReceiveWork()
    {
        using var admin = _factory.CreateClient();
        var adminLogin = await LoginAsync(admin, Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);

        var agent = await EnrollAsync(admin, allowDeployments: true);
        using var agentClient = AgentClient(agent.ApiKey);
        var package = await UploadPackageAsync(admin, $"revoked-{Guid.NewGuid():N}"[..20], "1.0.0", CreateZip());
        var now = DateTimeOffset.UtcNow;
        var changeResponse = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Change", "Deploy with revoked grant", "The agent grant is removed before work retrieval.",
            null, null, null, null, null, null, null, "Low risk.", "test",
            now.AddHours(1), now.AddHours(2), null));
        changeResponse.EnsureSuccessStatusCode();
        var change = (await changeResponse.Content.ReadFromJsonAsync<TicketDto>())!;
        var requestResponse = await admin.PostAsJsonAsync("/api/deployments",
            new CreateDeploymentRequest(agent.AgentId, package.Id, change.Id, null, null, "/srv/support360/revoked"));
        requestResponse.EnsureSuccessStatusCode();
        var deployment = (await requestResponse.Content.ReadFromJsonAsync<DeploymentRequestDto>())!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
            var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenant.SetSystem();
            var target = await db.Agents.SingleAsync(x => x.Id == agent.AgentId);
            target.DeploymentExecutionAllowed = false;
            await db.SaveChangesAsync();
        }

        var approver = await CreateApproverAsync(adminLogin.User.TenantId);
        using var approverClient = _factory.CreateClient();
        var approverLogin = await LoginAsync(approverClient, approver.Email, approver.Password);
        approverClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", approverLogin.AccessToken);
        (await approverClient.PostAsJsonAsync($"/api/tickets/{change.Id}/decision",
            new DecideTicketRequest("Approved", "Approved to test the removed deployment grant."))).EnsureSuccessStatusCode();

        var manifest = await agentClient.GetAsync($"/api/agent/v1/deployments/{deployment.Id}/manifest");
        Assert.Equal(HttpStatusCode.Forbidden, manifest.StatusCode);
        var artifact = await agentClient.GetAsync($"/api/agent/v1/deployments/{deployment.Id}/artifact");
        Assert.Equal(HttpStatusCode.Forbidden, artifact.StatusCode);
    }

    private static bool VerifyManifestSignature(DeploymentManifestDto manifest)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(Support360ApiFactory.DeploymentSigningPublicKey);
        var payload = DeploymentManifestService.BuildCanonicalPayload(manifest.DeploymentId, manifest.AgentId,
            manifest.TenantId, manifest.PackageName, manifest.PackageVersion, manifest.PackageSha256,
            manifest.TargetPath ?? string.Empty, manifest.Nonce, manifest.IssuedAt, manifest.ExpiresAt);
        return rsa.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromBase64String(manifest.Signature),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }

    private async Task<EnrollAgentResponse> EnrollAsync(HttpClient admin, bool allowDeployments)
    {
        var enrollmentResponse = await admin.PostAsJsonAsync("/api/enrollments",
            new CreateEnrollmentRequest("Linux", allowDeployments));
        enrollmentResponse.EnsureSuccessStatusCode();
        var enrollment = (await enrollmentResponse.Content.ReadFromJsonAsync<EnrollmentCreatedResponse>())!;
        using var agentClient = _factory.CreateClient();
        var capabilities = allowDeployments ? new[] { "metrics", "deployment_execution" } : new[] { "metrics" };
        var enrollResponse = await agentClient.PostAsJsonAsync("/api/agent/v1/enroll", new EnrollAgentRequest(
            enrollment.Token, $"manifest-{Guid.NewGuid():N}"[..24], "Linux", "Ubuntu 24.04", "amd64", "0.2.0", capabilities));
        enrollResponse.EnsureSuccessStatusCode();
        return (await enrollResponse.Content.ReadFromJsonAsync<EnrollAgentResponse>())!;
    }

    private HttpClient AgentClient(string apiKey)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    private static async Task<DeploymentPackageDto> UploadPackageAsync(HttpClient client, string name, string version, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent(version), "version");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", $"{name}.zip");
        var response = await client.PostAsync("/api/deployment-packages", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeploymentPackageDto>())!;
    }

    private static byte[] CreateZip()
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("index.html").Open());
            writer.Write("<html>support360</html>");
        }
        return bytes.ToArray();
    }

    private async Task<(string Email, string Password)> CreateApproverAsync(Guid tenantId)
    {
        var email = $"manifest-approver-{Guid.NewGuid():N}@support360.local";
        const string password = "Manifest-Approver-Password-123!";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        tenant.SetSystem();
        db.Users.Add(new User
        {
            TenantId = tenantId, Email = email, EmailNormalized = email.ToLowerInvariant(),
            DisplayName = "Manifest Approver", PasswordHash = hasher.Hash(password), Role = UserRole.Approver
        });
        await db.SaveChangesAsync();
        return (email, password);
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
