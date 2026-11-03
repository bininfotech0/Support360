using System.Net;
using System.Net.Http.Json;
using Support360.Contracts.Agents;
using Support360.Contracts.Auth;

namespace Support360.Tests.Integration;

public sealed class PostgreSqlEnrollmentTests
{
    [Fact]
    public async Task EnrollmentTransactionSupportsPostgresRetryingExecutionStrategy()
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPPORT360_TEST_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // This test is activated by the PostgreSQL CI service and the documented Docker verification command.
            return;
        }

        await using var factory = new Support360ApiFactory("Postgres", connectionString);
        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(
            Support360ApiFactory.AdminEmail,
            Support360ApiFactory.AdminPassword));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", login!.AccessToken);

        var enrollmentResponse = await client.PostAsJsonAsync("/api/enrollments", new CreateEnrollmentRequest("Linux"));
        enrollmentResponse.EnsureSuccessStatusCode();
        var enrollment = await enrollmentResponse.Content.ReadFromJsonAsync<EnrollmentCreatedResponse>();
        Assert.NotNull(enrollment);

        using var agentClient = factory.CreateClient();
        var enrolledResponse = await agentClient.PostAsJsonAsync("/api/agent/v1/enroll", new EnrollAgentRequest(
            enrollment!.Token, "postgres-e2e-agent", "Linux", "Alpine", "amd64", "0.2.0", ["metrics"]));
        enrolledResponse.EnsureSuccessStatusCode();
        var enrolled = await enrolledResponse.Content.ReadFromJsonAsync<EnrollAgentResponse>();
        Assert.NotNull(enrolled);

        var replay = await agentClient.PostAsJsonAsync("/api/agent/v1/enroll", new EnrollAgentRequest(
            enrollment.Token, "postgres-replay", "Linux", "Alpine", "amd64", "0.2.0", ["metrics"]));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }
}
