using Support360.Contracts.Agents;
using Support360.Contracts.Applications;
using Support360.Contracts.Auth;
using Support360.Contracts.Dashboard;
using Support360.Contracts.Telemetry;
using Support360.Contracts.Operations;
using Support360.Contracts.Jobs;
using Support360.Contracts.Deployments;

namespace Support360.Application.Abstractions;

public sealed record RequestActor(
    Guid? UserId,
    Guid TenantId,
    string DisplayName,
    Support360.Domain.UserRole Role,
    string? IpAddress,
    string Subject);

public sealed record AgentIdentity(Guid AgentId, Guid TenantId);

public interface IEnrollmentService
{
    Task<EnrollmentCreatedResponse> CreateEnrollmentAsync(
        RequestActor actor,
        CreateEnrollmentRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EnrollmentListItemDto>> ListEnrollmentsAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<EnrollAgentResponse> EnrollAsync(
        EnrollAgentRequest request,
        string? ipAddress,
        CancellationToken cancellationToken);
}

public interface IAgentService
{
    Task<IReadOnlyList<AgentSummaryDto>> ListAgentsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<AgentDetailResponse?> GetAgentAsync(Guid tenantId, Guid agentId, CancellationToken cancellationToken);
    Task<AgentMetricsResponse> GetMetricsAsync(Guid tenantId, Guid agentId, string range, CancellationToken cancellationToken);
    Task<RotateCredentialResponse> RotateCredentialAsync(RequestActor actor, Guid agentId, CancellationToken cancellationToken);
    Task<RevokeAgentResponse> RevokeAgentAsync(RequestActor actor, Guid agentId, CancellationToken cancellationToken);
}

public interface ITelemetryIngestionService
{
    Task<HeartbeatResponse> HeartbeatAsync(
        AgentIdentity identity,
        HeartbeatRequest request,
        CancellationToken cancellationToken);

    Task<TelemetryIngestResponse> IngestTelemetryAsync(
        AgentIdentity identity,
        TelemetryBatchRequest request,
        CancellationToken cancellationToken);

    Task<JobEventsResponse> IngestJobEventsAsync(
        AgentIdentity identity,
        JobEventBatchRequest request,
        CancellationToken cancellationToken);
}

public interface IApplicationCheckService
{
    Task<IReadOnlyList<ApplicationCheckDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<ApplicationCheckDto> CreateAsync(RequestActor actor, CreateApplicationCheckRequest request, CancellationToken cancellationToken);
    Task<ApplicationCheckDto?> UpdateAsync(RequestActor actor, Guid checkId, UpdateApplicationCheckRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(RequestActor actor, Guid checkId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApplicationCheckHistoryItemDto>> HistoryAsync(Guid tenantId, Guid checkId, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentApplicationCheckDto>> GetAgentChecksAsync(AgentIdentity identity, CancellationToken cancellationToken);
    Task<ApplicationCheckResultsResponse> IngestResultsAsync(AgentIdentity identity, ApplicationCheckResultsRequest request, CancellationToken cancellationToken);
    Task<HttpCheckTargetPolicyDto> GetHttpCheckTargetPolicyAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<HttpCheckTargetPolicyDto> UpdateHttpCheckTargetPolicyAsync(RequestActor actor, UpdateHttpCheckTargetPolicyRequest request, CancellationToken cancellationToken);
}

public interface IBatchJobService
{
    Task<BatchJobDto> CreateAsync(RequestActor actor, ConfigureBatchJobRequest request, CancellationToken cancellationToken);
    Task<BatchJobDto?> UpdateAsync(RequestActor actor, Guid jobId, UpdateBatchJobRequest request, CancellationToken cancellationToken);
}

public interface IDeploymentPackageService
{
    Task<IReadOnlyList<DeploymentPackageDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<DeploymentPackageDto?> GetAsync(Guid tenantId, Guid packageId, CancellationToken cancellationToken);
    Task<DeploymentPackageDto> UploadAsync(RequestActor actor, UploadDeploymentPackageRequest request, CancellationToken cancellationToken);
}

public interface IDeploymentService
{
    Task<IReadOnlyList<DeploymentRequestDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<DeploymentRequestDto?> GetAsync(Guid tenantId, Guid deploymentId, CancellationToken cancellationToken);
    Task<DeploymentRequestDto> CreateAsync(RequestActor actor, CreateDeploymentRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<DeploymentRequestDto>> PendingDeploymentsForAgentAsync(Guid agentId, CancellationToken cancellationToken);
    Task UpdateDeploymentResultAsync(Guid tenantId, Guid deploymentId, string status, string? detail, CancellationToken cancellationToken);
}

public interface IIncidentChangeService
{
    Task<IReadOnlyList<IncidentDto>> ListIncidentsAsync(Guid tenantId, string? status, CancellationToken cancellationToken);
    Task<IncidentDto> CreateIncidentAsync(RequestActor actor, CreateIncidentRequest request, CancellationToken cancellationToken);
    Task<IncidentDto?> TransitionIncidentAsync(RequestActor actor, Guid incidentId, TransitionIncidentRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChangeRequestDto>> ListChangeRequestsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<ChangeRequestDto> CreateChangeRequestAsync(RequestActor actor, CreateChangeRequest request, CancellationToken cancellationToken);
    Task<ChangeRequestDto?> DecideChangeRequestAsync(RequestActor actor, Guid changeRequestId, DecideChangeRequest request, CancellationToken cancellationToken);
    Task<ChangeRequestDto?> CancelChangeRequestAsync(RequestActor actor, Guid changeRequestId, CancellationToken cancellationToken);
    Task<ChangeApprovalPolicyDto> GetPolicyAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<ChangeApprovalPolicyDto> UpdatePolicyAsync(RequestActor actor, UpdateChangeApprovalPolicyRequest request, CancellationToken cancellationToken);
}

public interface IServiceRequestService
{
    Task<IReadOnlyList<ServiceRequestDto>> ListServiceRequestsAsync(Guid tenantId, string? status, CancellationToken cancellationToken);
    Task<ServiceRequestDto> CreateServiceRequestAsync(RequestActor actor, CreateServiceRequest request, CancellationToken cancellationToken);
    Task<ServiceRequestDto?> TransitionServiceRequestAsync(RequestActor actor, Guid serviceRequestId, TransitionServiceRequest request, CancellationToken cancellationToken);
}

public interface IDashboardService
{
    Task<OverviewDto> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlertDto>> ListAlertsAsync(Guid tenantId, string? status, CancellationToken cancellationToken);
    Task<bool> AcknowledgeAlertAsync(RequestActor actor, Guid alertId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AlertRuleDto>> ListAlertRulesAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<AlertRuleDto> CreateAlertRuleAsync(RequestActor actor, CreateAlertRuleRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEventDto>> ListAuditAsync(Guid tenantId, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<BatchJobDto>> ListBatchJobsAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<UsageSummaryDto> GetUsageAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface IAuditService
{
    Task WriteAsync(
        Guid tenantId,
        Support360.Domain.AuditActorType actorType,
        Guid? actorId,
        string? actorDisplay,
        string action,
        string targetType,
        string? targetId,
        string outcome,
        object? details,
        string? ipAddress,
        CancellationToken cancellationToken);
}

public interface IAlertEvaluationService
{
    Task<int> EvaluateAsync(CancellationToken cancellationToken);
}

public interface IAgentLivenessService
{
    Task<int> MarkStaleAgentsOfflineAsync(CancellationToken cancellationToken);
}

public interface IRetentionService
{
    Task<int> ApplyAsync(CancellationToken cancellationToken);
}

public interface IUserAuthenticationService
{
    Task<(LoginResponse? Response, string? Failure)> LoginAsync(string email, string password, string? ipAddress, CancellationToken cancellationToken);
}

public interface IUserTokenIssuer
{
    (string Token, DateTimeOffset ExpiresAt) Issue(Support360.Domain.Entities.User user, Support360.Domain.Entities.Tenant tenant);
}
