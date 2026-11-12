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
    DbSet<Ticket> Tickets { get; }
    DbSet<TicketAlert> TicketAlerts { get; }
    DbSet<TicketApproval> TicketApprovals { get; }
    DbSet<TicketActivity> TicketActivities { get; }
    DbSet<TicketLink> TicketLinks { get; }
    DbSet<TicketComment> TicketComments { get; }
    DbSet<TicketWorklog> TicketWorklogs { get; }
    DbSet<TicketWatcher> TicketWatchers { get; }
    DbSet<TicketAttachment> TicketAttachments { get; }
    DbSet<TicketNumberSequence> TicketNumberSequences { get; }
    DbSet<Project> Projects { get; }
    DbSet<WorkDnaCategory> WorkDnaCategories { get; }
    DbSet<WorkActivity> WorkActivities { get; }
    DbSet<WorkActivitySchedule> WorkActivitySchedules { get; }
    DbSet<WorkCalendarDay> WorkCalendarDays { get; }
    DbSet<WorkLeave> WorkLeaves { get; }
    DbSet<DeploymentPackage> DeploymentPackages { get; }
    DbSet<DeploymentManifestNonce> DeploymentManifestNonces { get; }
    DbSet<DeploymentRequest> DeploymentRequests { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<UsageEvent> UsageEvents { get; }
    DatabaseFacade Database { get; }

    void ClearTracking();
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
