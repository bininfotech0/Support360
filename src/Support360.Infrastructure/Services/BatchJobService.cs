using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Contracts.Dashboard;
using Support360.Contracts.Jobs;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class BatchJobService : IBatchJobService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IRealtimeNotifier _realtime;
    private readonly IValidator<ConfigureBatchJobRequest> _createValidator;
    private readonly IValidator<UpdateBatchJobRequest> _updateValidator;

    public BatchJobService(ISupport360DbContext db, ITenantContext tenantContext, IClock clock, IAuditService audit,
        IRealtimeNotifier realtime, IValidator<ConfigureBatchJobRequest> createValidator, IValidator<UpdateBatchJobRequest> updateValidator)
    {
        _db = db; _tenantContext = tenantContext; _clock = clock; _audit = audit; _realtime = realtime;
        _createValidator = createValidator; _updateValidator = updateValidator;
    }

    public async Task<BatchJobDto> CreateAsync(RequestActor actor, ConfigureBatchJobRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(actor.TenantId);
        var agent = await _db.Agents.AsNoTracking().SingleOrDefaultAsync(
            a => a.TenantId == actor.TenantId && a.Id == request.AgentId && a.Status == AgentStatus.Active, cancellationToken)
            ?? throw new NotFoundException("Active agent not found.");
        var key = request.JobKey.Trim();
        if (await _db.BatchJobs.AnyAsync(j => j.TenantId == actor.TenantId && j.AgentId == agent.Id && j.JobKey == key, cancellationToken))
            throw new ConflictException("A job with this key is already configured for the agent.");
        var now = _clock.UtcNow;
        var job = new BatchJob
        {
            TenantId = actor.TenantId, AgentId = agent.Id, JobKey = key, Name = request.Name.Trim(),
            Scheduler = Clean(request.Scheduler), ExpectedSchedule = Clean(request.ExpectedSchedule), IsConfigured = true, CreatedAt = now
        };
        _db.BatchJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "batch-job.configured", "BatchJob", job.Id.ToString(), "success",
            new { job.AgentId, job.JobKey, job.Name, job.Scheduler, job.ExpectedSchedule }, actor.IpAddress, cancellationToken);
        await _realtime.AgentChangedAsync(actor.TenantId, agent.Id, cancellationToken);
        return ToDto(job, agent.Hostname);
    }

    public async Task<BatchJobDto?> UpdateAsync(RequestActor actor, Guid jobId, UpdateBatchJobRequest request, CancellationToken cancellationToken)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(actor.TenantId);
        var job = await _db.BatchJobs.Include(j => j.Agent).SingleOrDefaultAsync(
            j => j.TenantId == actor.TenantId && j.Id == jobId, cancellationToken);
        if (job is null) return null;
        var previous = new { job.Name, job.Scheduler, job.ExpectedSchedule };
        job.Name = request.Name.Trim(); job.Scheduler = Clean(request.Scheduler); job.ExpectedSchedule = Clean(request.ExpectedSchedule);
        job.IsConfigured = true;
        job.UpdatedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "batch-job.updated", "BatchJob", job.Id.ToString(), "success",
            new { Previous = previous, Current = new { job.Name, job.Scheduler, job.ExpectedSchedule } }, actor.IpAddress, cancellationToken);
        await _realtime.AgentChangedAsync(actor.TenantId, job.AgentId, cancellationToken);
        return ToDto(job, job.Agent.Hostname);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static BatchJobDto ToDto(BatchJob job, string hostname) => new(
        job.Id, job.JobKey, job.Name, job.Scheduler, job.ExpectedSchedule, job.AgentId, hostname,
        null, null, null, []);
}
