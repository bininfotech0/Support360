using Support360.Agent;

namespace Support360.Tests.Agent;

public sealed class PersistentQueueTests
{
    [Fact]
    public void TelemetryQueuePersistsCapsAndAcknowledges()
    {
        var path = Path.Combine(Path.GetTempPath(), "support360-test-queue-" + Guid.NewGuid().ToString("N"), "queue.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var queue = PersistentQueue<MetricSample>.Open(path, 5000, 200, "telemetry queue file is invalid");
        var samples = new MetricSample[5005];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = new MetricSample(index.ToString(), "cpu.usage.percent", 1, "percent", DateTimeOffset.UtcNow);
        }
        queue.Add(samples);

        Assert.Equal(5000, queue.Len);

        var batch = queue.Batch();
        Assert.Equal(200, batch.Count);

        queue.Acknowledge(batch.Count);

        var reloaded = PersistentQueue<MetricSample>.Open(path, 5000, 200, "telemetry queue file is invalid");
        Assert.Equal(5000 - 200, reloaded.Len);
    }

    [Fact]
    public void ResultQueuePersistsCapsAndAcknowledges()
    {
        var path = Path.Combine(Path.GetTempPath(), "support360-test-results-" + Guid.NewGuid().ToString("N"), "results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var queue = PersistentQueue<ApplicationCheckResult>.Open(path, 500, 100, "application result queue file is invalid");
        var results = new ApplicationCheckResult[501];
        for (var index = 0; index < results.Length; index++)
        {
            results[index] = new ApplicationCheckResult("check-1", index.ToString(), "Healthy",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, null, null);
        }
        queue.Add(results);

        Assert.Equal(500, queue.Len);

        var batch = queue.Batch();
        Assert.Equal(100, batch.Count);

        queue.Acknowledge(batch.Count);

        var reloaded = PersistentQueue<ApplicationCheckResult>.Open(path, 500, 100, "application result queue file is invalid");
        Assert.Equal(500 - 100, reloaded.Len);
    }

    [Fact]
    public void QueueRejectsInvalidAcknowledgement()
    {
        var queue = PersistentQueue<MetricSample>.Open(
            Path.Combine(Path.GetTempPath(), "support360-test-ack-" + Guid.NewGuid().ToString("N"), "queue.json"),
            5000, 200, "telemetry queue file is invalid");
        Assert.Throws<InvalidOperationException>(() => queue.Acknowledge(1));
        Assert.Throws<InvalidOperationException>(() => queue.Acknowledge(-1));
    }

    [Fact]
    public void QueueRejectsInvalidJsonOnReopen()
    {
        var path = Path.Combine(Path.GetTempPath(), "support360-test-invalid-" + Guid.NewGuid().ToString("N"), "queue.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not json");

        Assert.Throws<InvalidDataException>(() =>
            PersistentQueue<MetricSample>.Open(path, 5000, 200, "telemetry queue file is invalid"));
    }
}
