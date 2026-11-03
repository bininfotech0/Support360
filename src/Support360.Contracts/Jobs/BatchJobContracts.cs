namespace Support360.Contracts.Jobs;

public sealed record ConfigureBatchJobRequest(
    Guid AgentId,
    string JobKey,
    string Name,
    string? Scheduler,
    string? ExpectedSchedule);

public sealed record UpdateBatchJobRequest(
    string Name,
    string? Scheduler,
    string? ExpectedSchedule);
