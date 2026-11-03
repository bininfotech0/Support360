using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Application.Options;
using Support360.Application.Security;
using Support360.Contracts.Agents;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class EnrollmentService : IEnrollmentService
{
    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretGenerator _secretGenerator;
    private readonly ISecretHasher _secretHasher;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IRealtimeNotifier _realtime;
    private readonly AgentPlatformOptions _options;
    private readonly IValidator<CreateEnrollmentRequest> _createValidator;
    private readonly IValidator<EnrollAgentRequest> _enrollValidator;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(
        ISupport360DbContext db,
        ITenantContext tenantContext,
        ISecretGenerator secretGenerator,
        ISecretHasher secretHasher,
        IClock clock,
        IAuditService audit,
        IRealtimeNotifier realtime,
        IOptions<AgentPlatformOptions> options,
        IValidator<CreateEnrollmentRequest> createValidator,
        IValidator<EnrollAgentRequest> enrollValidator,
        ILogger<EnrollmentService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _secretGenerator = secretGenerator;
        _secretHasher = secretHasher;
        _clock = clock;
        _audit = audit;
        _realtime = realtime;
        _options = options.Value;
        _createValidator = createValidator;
        _enrollValidator = enrollValidator;
        _logger = logger;
    }

    public async Task<EnrollmentCreatedResponse> CreateEnrollmentAsync(
        RequestActor actor,
        CreateEnrollmentRequest request,
        CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenantContext.SetTenant(actor.TenantId);

        AgentPlatform? targetPlatform = null;
        if (!string.IsNullOrWhiteSpace(request.TargetPlatform))
        {
            targetPlatform = Enum.Parse<AgentPlatform>(request.TargetPlatform, ignoreCase: true);
        }

        var now = _clock.UtcNow;
        var token = _secretGenerator.Generate(32);
        var enrollment = new AgentEnrollment
        {
            TenantId = actor.TenantId,
            TokenHash = _secretHasher.Hash(token),
            TokenPrefix = token[..8],
            CreatedByUserId = actor.UserId ?? Guid.Empty,
            TargetPlatform = targetPlatform,
            AllowDeploymentExecution = request.AllowDeploymentExecution,
            ExpiresAt = now.AddMinutes(_options.EnrollmentLifetimeMinutes),
            CreatedAt = now
        };

        _db.AgentEnrollments.Add(enrollment);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            actor.TenantId,
            AuditActorType.User,
            actor.UserId,
            actor.DisplayName,
            "agent.enrollment.created",
            "AgentEnrollment",
            enrollment.Id.ToString(),
            "success",
            new { enrollment.TargetPlatform, enrollment.AllowDeploymentExecution, enrollment.ExpiresAt },
            actor.IpAddress,
            cancellationToken);

        return new EnrollmentCreatedResponse(
            enrollment.Id,
            token,
            enrollment.TokenPrefix,
            targetPlatform is null ? null : new AgentPlatformDto(targetPlatform.Value.ToString()),
            enrollment.ExpiresAt,
            $"support360-agent enroll --server <SUPPORT360_URL> --token {token}{(enrollment.AllowDeploymentExecution ? " --enable-deployments" : string.Empty)}",
            enrollment.AllowDeploymentExecution);
    }

    public async Task<IReadOnlyList<EnrollmentListItemDto>> ListEnrollmentsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        _tenantContext.SetTenant(tenantId);
        var now = _clock.UtcNow;
        var enrollments = await _db.AgentEnrollments
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return enrollments.Select(e => new EnrollmentListItemDto(
            e.Id,
            e.TokenPrefix,
            e.TargetPlatform?.ToString(),
            e.ExpiresAt,
            e.ConsumedAt,
            e.ConsumedByAgentId,
            e.IsUsable(now),
            e.AllowDeploymentExecution)).ToArray();
    }

    public async Task<EnrollAgentResponse> EnrollAsync(
        EnrollAgentRequest request,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        await _enrollValidator.ValidateAndThrowAsync(request, cancellationToken);

        var now = _clock.UtcNow;
        var tokenHash = _secretHasher.Hash(request.Token.Trim());
        var enrollment = await _db.AgentEnrollments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(e => e.Tenant)
            .SingleOrDefaultAsync(e => e.TokenHash == tokenHash, cancellationToken);

        if (enrollment is null || !enrollment.IsUsable(now))
        {
            throw new InvalidCredentialException("Enrollment credential is invalid, expired, revoked or already used.");
        }

        if (enrollment.Tenant.Status != TenantStatus.Active)
        {
            throw new InvalidCredentialException("The tenant is not active.");
        }

        var platform = Enum.Parse<AgentPlatform>(request.Platform, ignoreCase: true);
        if (enrollment.TargetPlatform is not null && enrollment.TargetPlatform != platform)
        {
            throw new InvalidCredentialException("The enrollment credential is restricted to a different platform.");
        }

        var tenantId = enrollment.TenantId;
        var enrollmentId = enrollment.Id;
        _tenantContext.SetTenant(tenantId);

        var apiKey = _secretGenerator.Generate(48);
        var agent = new Agent
        {
            TenantId = tenantId,
            Hostname = request.Hostname.Trim(),
            Platform = platform,
            OsVersion = request.OsVersion,
            Architecture = request.Architecture,
            Version = request.AgentVersion,
            Status = AgentStatus.Active,
            DeploymentExecutionAllowed = enrollment.AllowDeploymentExecution,
            Connectivity = AgentConnectivity.Unknown,
            EnrolledAt = now,
            CapabilitiesJson = request.Capabilities is null ? null : JsonSerializer.Serialize(request.Capabilities),
            CreatedAt = now
        };

        var credential = new AgentCredential
        {
            TenantId = tenantId,
            AgentId = agent.Id,
            KeyHash = _secretHasher.Hash(apiKey),
            KeyPrefix = apiKey[..8],
            ExpiresAt = _options.AgentCredentialLifetimeDays > 0
                ? now.AddDays(_options.AgentCredentialLifetimeDays)
                : null,
            CreatedAt = now
        };

        var response = new EnrollAgentResponse(
            agent.Id,
            tenantId,
            apiKey,
            credential.KeyPrefix,
            _options.HeartbeatIntervalSeconds,
            _options.TelemetryIntervalSeconds,
            MetricCatalog.Allowed,
            now);

        var executionStrategy = _db.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(async () =>
        {
            // A retry starts with a clean tracker. If a prior commit succeeded but its
            // acknowledgement was lost, the stable agent ID and key hash verify it.
            _db.ClearTracking();
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var alreadyCreated = await _db.Agents.IgnoreQueryFilters()
                .AnyAsync(a => a.Id == agent.Id && a.TenantId == tenantId, cancellationToken);
            if (alreadyCreated)
            {
                var credentialExists = await _db.AgentCredentials.IgnoreQueryFilters()
                    .AnyAsync(c => c.AgentId == agent.Id && c.KeyHash == credential.KeyHash, cancellationToken);
                if (!credentialExists)
                {
                    throw new InvalidOperationException("Enrollment retry found an incomplete agent identity.");
                }

                await transaction.CommitAsync(cancellationToken);
                return response;
            }

            _db.Agents.Add(agent);
            _db.AgentCredentials.Add(credential);
            var consumed = await _db.AgentEnrollments
                .IgnoreQueryFilters()
                .Where(e => e.Id == enrollmentId
                            && e.ConsumedAt == null
                            && e.RevokedAt == null
                            && e.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(e => e.ConsumedAt, now)
                    .SetProperty(e => e.ConsumedByAgentId, agent.Id)
                    .SetProperty(e => e.UpdatedAt, now), cancellationToken);

            if (consumed != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new ConflictException("Enrollment credential was consumed concurrently. Request a new credential.");
            }

            await _db.SaveChangesAsync(cancellationToken);
            await _audit.WriteAsync(
                tenantId,
                AuditActorType.Agent,
                agent.Id,
                agent.Hostname,
                "agent.enrolled",
                "Agent",
                agent.Id.ToString(),
                "success",
                new { agent.Hostname, Platform = platform.ToString(), agent.Version, ipAddress },
                ipAddress,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return response;
        });

        await _realtime.AgentChangedAsync(tenantId, agent.Id, cancellationToken);
        _logger.LogInformation("Agent {AgentId} enrolled for tenant {TenantId} from {IpAddress}", agent.Id, tenantId, ipAddress);
        return response;
    }
}
