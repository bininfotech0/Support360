using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using Support360.Contracts.Auth;
using Support360.Contracts.Deployments;

namespace Support360.Tests.Integration;

public sealed class DeploymentPackageTests : IClassFixture<Support360ApiFactory>
{
    private readonly Support360ApiFactory _factory;
    public DeploymentPackageTests(Support360ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TenantAdminCanUploadBoundedZipAndCatalogReturnsDigestWithoutContent()
    {
        using var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword));
        loginResponse.EnsureSuccessStatusCode();
        var login = (await loginResponse.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var packageBytes = CreateZip();

        using var form = MakeForm("orders-api", "2.4.1", packageBytes);
        var upload = await client.PostAsync("/api/deployment-packages", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var dto = (await upload.Content.ReadFromJsonAsync<DeploymentPackageDto>())!;
        Assert.Equal("orders-api", dto.Name);
        Assert.Equal("2.4.1", dto.Version);
        Assert.Equal(packageBytes.LongLength, dto.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant(), dto.Sha256);

        var catalog = await client.GetFromJsonAsync<List<DeploymentPackageDto>>("/api/deployment-packages");
        Assert.Contains(catalog!, item => item.Id == dto.Id && item.Sha256 == dto.Sha256);
        Assert.Null(typeof(DeploymentPackageDto).GetProperty("Content"));

        using var duplicateForm = MakeForm("orders-api", "2.4.1", packageBytes);
        var duplicate = await client.PostAsync("/api/deployment-packages", duplicateForm);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var invalidForm = MakeForm("bad-package", "1.0", Encoding.UTF8.GetBytes("not a zip archive"));
        var invalid = await client.PostAsync("/api/deployment-packages", invalidForm);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var traversalForm = MakeForm("unsafe-package", "1.0", CreateZip("../outside.txt"));
        var traversal = await client.PostAsync("/api/deployment-packages", traversalForm);
        Assert.Equal(HttpStatusCode.BadRequest, traversal.StatusCode);
    }

    private static MultipartFormDataContent MakeForm(string name, string version, byte[] content)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent(version), "version");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", $"{name}.zip");
        return form;
    }

    private static byte[] CreateZip(string entryName = "README.txt")
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry(entryName).Open()))
            writer.Write("Test artifact. It is not executed.");
        return memory.ToArray();
    }
}
