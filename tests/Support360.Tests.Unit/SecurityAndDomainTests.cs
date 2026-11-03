using Support360.Application.Security;
using Support360.Domain;
using Support360.Domain.Entities;
using Support360.Infrastructure.Security;

namespace Support360.Tests.Unit;

public sealed class SecretServicesTests
{
    [Fact]
    public void GeneratedSecretsAreUrlSafeAndSufficientlyLong()
    {
        var generator = new CryptographicSecretGenerator();

        var first = generator.Generate(48);
        var second = generator.Generate(48);

        Assert.NotEqual(first, second);
        Assert.True(first.Length >= 64);
        Assert.DoesNotContain('+', first);
        Assert.DoesNotContain('/', first);
        Assert.DoesNotContain('=', first);
    }

    [Fact]
    public void HasherIsDeterministicAndCaseSensitive()
    {
        var hasher = new Sha256SecretHasher();

        Assert.Equal(hasher.Hash("secret-value"), hasher.Hash("secret-value"));
        Assert.NotEqual(hasher.Hash("secret-value"), hasher.Hash("Secret-value"));
        Assert.Equal(64, hasher.Hash("secret-value").Length);
    }

    [Fact]
    public void PasswordHashVerifiesOnlyTheCorrectPassword()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var hash = hasher.Hash("Correct-Horse-Battery-Staple");

        Assert.True(hasher.Verify("Correct-Horse-Battery-Staple", hash));
        Assert.False(hasher.Verify("wrong-password", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$notanumber$c2FsdA==$a2V5")]
    public void MalformedPasswordHashesAreRejected(string hash)
    {
        var hasher = new Pbkdf2PasswordHasher();

        Assert.False(hasher.Verify("anything", hash));
    }
}

public sealed class MetricCatalogTests
{
    [Fact]
    public void EveryAllowedMetricHasAnExactUnit()
    {
        Assert.Equal(MetricCatalog.Units.Count, MetricCatalog.Allowed.Count);

        foreach (var metric in MetricCatalog.Allowed)
        {
            Assert.True(MetricCatalog.Units.TryGetValue(metric, out var unit));
            Assert.False(string.IsNullOrWhiteSpace(unit));
        }
    }

    [Fact]
    public void UnknownMetricIsNotAllowed()
    {
        Assert.False(MetricCatalog.Units.ContainsKey("free.form.command"));
    }
}

public sealed class AgentDomainTests
{
    [Fact]
    public void HeartbeatMarksAgentOnlineAndPreservesMissingFields()
    {
        var agent = new Agent
        {
            Hostname = "host-1",
            Version = "1.0.0",
            LastHeartbeatAt = null,
            CollectorHealthy = false
        };
        var now = DateTimeOffset.UtcNow;

        agent.RecordHeartbeat(now, null, "Ubuntu 24.04", null, null, true, "ok", 123);

        Assert.Equal(AgentConnectivity.Online, agent.Connectivity);
        Assert.Equal(now, agent.LastHeartbeatAt);
        Assert.Equal("1.0.0", agent.Version);
        Assert.Equal("Ubuntu 24.04", agent.OsVersion);
        Assert.True(agent.CollectorHealthy);
        Assert.Equal(123, agent.UptimeSeconds);
    }

    [Fact]
    public void RevokedCredentialIsNeverUsable()
    {
        var now = DateTimeOffset.UtcNow;
        var credential = new AgentCredential
        {
            KeyHash = "hash",
            KeyPrefix = "prefix",
            RevokedAt = now.AddMinutes(-1)
        };

        Assert.False(credential.IsUsable(now));
    }

    [Fact]
    public void ExpiredEnrollmentIsNotUsable()
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = new AgentEnrollment
        {
            TokenHash = "hash",
            TokenPrefix = "prefix",
            ExpiresAt = now.AddMinutes(-1)
        };

        Assert.False(enrollment.IsUsable(now));
    }

    [Fact]
    public void UnconsumedFutureEnrollmentIsUsable()
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = new AgentEnrollment
        {
            TokenHash = "hash",
            TokenPrefix = "prefix",
            ExpiresAt = now.AddMinutes(1)
        };

        Assert.True(enrollment.IsUsable(now));
    }
}
