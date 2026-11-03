using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Support360.Domain.Entities;

namespace Support360.Application.Abstractions;

public interface ISupport360DbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<Agent> Agents { get; }
    DbSet<AgentCredential> AgentCredentials { get; }
    DbSet<AgentEnrollment> AgentEnrollments { get; }
    DbSet<MetricSample> MetricSamples { get; }
    DbSet<AgentServiceStatus> AgentServiceStatuses { get; }
    DbSet<ApplicationCheck> ApplicationChecks { get; }
    DbSet<ApplicationCheckResult> ApplicationCheckResults { get; }
    DbSet<BatchJob> BatchJobs { get; }
    DbSet<BatchJobExecution> BatchJobExecutions { get; }
    DbSet<AlertRule> AlertRules { get; }
    DbSet<Alert> Alerts { get; }
    DbSet<Incident> Incidents { get; }
    DbSet<IncidentAlert> IncidentAlerts { get; }
    DbSet<ChangeRequest> ChangeRequests { get; }
    DbSet<ChangeApproval> ChangeApprovals { get; }
    DbSet<DeploymentPackage> DeploymentPackages { get; }
    DbSet<DeploymentRequest> DeploymentRequests { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<UsageEvent> UsageEvents { get; }
    DatabaseFacade Database { get; }

    void ClearTracking();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
