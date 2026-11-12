using System.Net;
using System.Net.Http.Json;
using Support360.Contracts.Auth;

namespace Support360.Tests.Integration;

public sealed class SecurityHeadersTests : IClassFixture<Support360ApiFactory>
{
    private readonly Support360ApiFactory _factory;

    public SecurityHeadersTests(Support360ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/api/agents")]
    [InlineData("/api/agents/00000000-0000-0000-0000-000000000000/metrics")]
    public async Task ApiResponsesCarryHardeningHeaders(string path)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);

        AssertHardeningHeaders(response);
    }

    [Fact]
    public async Task ProblemResponsesCarryHardeningHeaders()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("", ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertHardeningHeaders(response);
    }

    private static void AssertHardeningHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
    }
}
