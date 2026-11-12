using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Support360.Application.Abstractions;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Persistence;

public sealed class Support360DbContext : DbContext, ISupport360DbContext
{
    private readonly ITenantContext _tenantContext;

    public Support360DbContext(DbContextOptions<Support360DbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentCredential> AgentCredentials => Set<AgentCredential>();
    public DbSet<AgentEnrollment> AgentEnrollments => Set<AgentEnrollment>();
    public DbSet<MetricSample> MetricSamples => Set<MetricSample>();
    public DbSet<AgentServiceStatus> AgentServiceStatuses => Set<AgentServiceStatus>();
    public DbSet<ApplicationCheck> ApplicationChecks => Set<ApplicationCheck>();
    public DbSet<ApplicationCheckResult> ApplicationCheckResults => Set<ApplicationCheckResult>();
    public DbSet<BatchJob> BatchJobs => Set<BatchJob>();
    public DbSet<BatchJobExecution> BatchJobExecutions => Set<BatchJobExecution>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketAlert> TicketAlerts => Set<TicketAlert>();
    public DbSet<TicketApproval> TicketApprovals => Set<TicketApproval>();
    public DbSet<TicketActivity> TicketActivities => Set<TicketActivity>();
    public DbSet<TicketLink> TicketLinks => Set<TicketLink>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketWorklog> TicketWorklogs => Set<TicketWorklog>();
    public DbSet<TicketWatcher> TicketWatchers => Set<TicketWatcher>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<TicketNumberSequence> TicketNumberSequences => Set<TicketNumberSequence>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkDnaCategory> WorkDnaCategories => Set<WorkDnaCategory>();
    public DbSet<WorkActivity> WorkActivities => Set<WorkActivity>();
    public DbSet<WorkActivitySchedule> WorkActivitySchedules => Set<WorkActivitySchedule>();
    public DbSet<WorkCalendarDay> WorkCalendarDays => Set<WorkCalendarDay>();
    public DbSet<WorkLeave> WorkLeaves => Set<WorkLeave>();
    public DbSet<DeploymentPackage> DeploymentPackages => Set<DeploymentPackage>();
    public DbSet<DeploymentManifestNonce> DeploymentManifestNonces => Set<DeploymentManifestNonce>();
    public DbSet<DeploymentRequest> DeploymentRequests => Set<DeploymentRequest>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();

    public void ClearTracking() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("Tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(80).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.RequiredChangeApprovals).HasDefaultValue(1);
            e.Property(x => x.RequireChangeSeparationOfDuties).HasDefaultValue(true);
            e.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.EmailNormalized).HasMaxLength(320).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.EmailNormalized).IsUnique();
            e.HasIndex(x => x.TenantId);
            e.HasOne(x => x.Tenant).WithMany(x => x.Users).HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<Agent>(e =>
        {
            e.ToTable("Agents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Hostname).HasMaxLength(255).IsRequired();
            e.Property(x => x.Platform).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.OsVersion).HasMaxLength(255);
            e.Property(x => x.Architecture).HasMaxLength(64);
            e.Property(x => x.Version).HasMaxLength(64);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Connectivity).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.CapabilitiesJson).HasColumnType("text");
            e.Property(x => x.DeploymentExecutionAllowed).HasDefaultValue(false);
            e.Property(x => x.CollectorHealthDetail).HasMaxLength(2000);
            e.HasIndex(x => new { x.TenantId, x.Hostname });
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<AgentCredential>(e =>
        {
            e.ToTable("AgentCredentials");
            e.HasKey(x => x.Id);
            e.Property(x => x.KeyHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.KeyPrefix).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.HasIndex(x => new { x.AgentId, x.RevokedAt });
            e.HasOne(x => x.Agent).WithMany(x => x.Credentials).HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<AgentEnrollment>(e =>
        {
            e.ToTable("AgentEnrollments");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.TokenPrefix).HasMaxLength(16).IsRequired();
            e.Property(x => x.TargetPlatform).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.AllowDeploymentExecution).HasDefaultValue(false);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ExpiresAt });
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<MetricSample>(e =>
        {
            e.ToTable("MetricSamples");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Metric).HasMaxLength(100).IsRequired();
            e.Property(x => x.Unit).HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.AgentId, x.EventId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.AgentId, x.CapturedAt });
            e.HasIndex(x => new { x.TenantId, x.Metric, x.CapturedAt });
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<AgentServiceStatus>(e =>
        {
            e.ToTable("AgentServiceStatuses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.HasIndex(x => new { x.AgentId, x.Name }).IsUnique();
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<ApplicationCheck>(e =>
        {
            e.ToTable("ApplicationChecks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Target).HasMaxLength(2048).IsRequired();
            e.Property(x => x.LastStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.LastErrorCategory).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.AgentId, x.Name }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.AgentId, x.Enabled });
            e.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<ApplicationCheckResult>(e =>
        {
            e.ToTable("ApplicationCheckResults");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ErrorCategory).HasMaxLength(64);
            e.Property(x => x.ErrorDetail).HasMaxLength(1000);
            e.HasIndex(x => new { x.AgentId, x.EventId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ApplicationCheckId, x.StartedAt });
            e.HasOne(x => x.ApplicationCheck).WithMany(x => x.Results)
                .HasForeignKey(x => x.ApplicationCheckId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<BatchJob>(e =>
        {
            e.ToTable("BatchJobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.JobKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.Scheduler).HasMaxLength(255);
            e.Property(x => x.ExpectedSchedule).HasMaxLength(255);
            e.Property(x => x.IsConfigured).HasDefaultValue(false);
            e.HasIndex(x => new { x.AgentId, x.JobKey }).IsUnique();
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<BatchJobExecution>(e =>
        {
            e.ToTable("BatchJobExecutions");
            e.HasKey(x => x.Id);
            e.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.FailureReason).HasMaxLength(4000);
            e.HasIndex(x => new { x.AgentId, x.CorrelationId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.StartedAt });
            e.HasOne(x => x.BatchJob).WithMany(x => x.Executions).HasForeignKey(x => x.BatchJobId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<AlertRule>(e =>
        {
            e.ToTable("AlertRules");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Metric).HasMaxLength(100).IsRequired();
            e.Property(x => x.Operator).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.Enabled });
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<Alert>(e =>
        {
            e.ToTable("Alerts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Message).HasMaxLength(1000).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Status, x.TriggeredAt });
            e.HasIndex(x => new { x.AlertRuleId, x.AgentId, x.Status });
            e.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AlertRule).WithMany().HasForeignKey(x => x.AlertRuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<Ticket>(e =>
        {
            e.ToTable("Tickets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(8000).IsRequired();
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Impact).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Urgency).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Risk).HasMaxLength(4000);
            e.Property(x => x.TargetEnvironment).HasMaxLength(80);
            e.Property(x => x.RequestedBySubject).HasMaxLength(255);
            e.Property(x => x.RequestedByDisplay).HasMaxLength(200);
            e.Property(x => x.RequiredApprovals).HasDefaultValue(1);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Type, x.Status, x.OpenedAt });
            e.HasIndex(x => new { x.TenantId, x.Status, x.Priority, x.OpenedAt });
            e.HasIndex(x => new { x.TenantId, x.AssignedToUserId, x.Status });
            e.HasOne<BatchJobExecution>().WithMany().HasForeignKey(x => x.RelatedBatchJobExecutionId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<TicketAlert>(e =>
        {
            e.ToTable("TicketAlerts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.TicketId, x.AlertId }).IsUnique();
            e.HasOne(x => x.Ticket).WithMany(x => x.Alerts).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Alert).WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<TicketApproval>(e =>
        {
            e.ToTable("TicketApprovals");
            e.HasKey(x => x.Id);
            e.Property(x => x.ApproverSubject).HasMaxLength(255).IsRequired();
            e.Property(x => x.ApproverDisplay).HasMaxLength(200).IsRequired();
            e.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasIndex(x => new { x.TenantId, x.TicketId, x.ApproverSubject }).IsUnique();
            e.HasOne(x => x.Ticket).WithMany(x => x.Approvals).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<TicketActivity>(e =>
        {
            e.ToTable("TicketActivities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(64).IsRequired();
            e.Property(x => x.FromStatus).HasMaxLength(32);
            e.Property(x => x.ToStatus).HasMaxLength(32);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.Property(x => x.ActorDisplay).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.TicketId, x.OccurredAt });
            e.HasOne(x => x.Ticket).WithMany(x => x.Activities).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<TicketLink>(e =>
        {
            e.ToTable("TicketLinks");
            e.HasKey(x => x.Id);
            e.Property(x => x.LinkType).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.SourceTicketId, x.TargetTicketId, x.LinkType }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.TargetTicketId });
            e.HasOne(x => x.SourceTicket).WithMany().HasForeignKey(x => x.SourceTicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TargetTicket).WithMany().HasForeignKey(x => x.TargetTicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<TicketComment>(e =>
        {
            e.ToTable("TicketComments");
            e.HasKey(x => x.Id);
            e.Property(x => x.AuthorSubject).HasMaxLength(255);
            e.Property(x => x.AuthorDisplay).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(8000).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.TicketId, x.CreatedAt });
            e.HasOne(x => x.Ticket).WithMany(x => x.Comments).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<TicketWorklog>(e =>
        {
            e.ToTable("TicketWorklogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.AuthorSubject).HasMaxLength(255);
            e.Property(x => x.AuthorDisplay).HasMaxLength(200).IsRequired();
            e.Property(x => x.Note).HasMaxLength(2000);
            e.HasIndex(x => new { x.TenantId, x.TicketId, x.WorkedAt });
            e.HasOne(x => x.Ticket).WithMany(x => x.Worklogs).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<TicketWatcher>(e =>
        {
            e.ToTable("TicketWatchers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Subject).HasMaxLength(255).IsRequired();
            e.Property(x => x.Display).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.TicketId, x.Subject }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.UserId });
            e.HasOne(x => x.Ticket).WithMany(x => x.Watchers).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<TicketAttachment>(e =>
        {
            e.ToTable("TicketAttachments");
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.UploadedByDisplay).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.TicketId, x.UploadedAt });
            e.HasOne(x => x.Ticket).WithMany(x => x.Attachments).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<TicketNumberSequence>(e =>
        {
            e.ToTable("TicketNumberSequences");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Type }).IsUnique();
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("Projects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<WorkDnaCategory>(e =>
        {
            e.ToTable("WorkDnaCategories");
            e.HasKey(x => x.Id);
            e.Property(x => x.CategoryName).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActivityName).HasMaxLength(100).IsRequired();
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasIndex(x => new { x.TenantId, x.CategoryName, x.ActivityName }).IsUnique();
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<WorkActivity>(e =>
        {
            e.ToTable("WorkActivities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(255).IsRequired();
            e.Property(x => x.Description).HasMaxLength(8000);
            e.Property(x => x.TargetMode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.BusinessImpact).HasMaxLength(4000);
            e.HasIndex(x => new { x.TenantId, x.StartTime });
            e.HasIndex(x => new { x.TenantId, x.AssigneeId, x.StartTime });
            e.HasIndex(x => new { x.TenantId, x.ScheduleId, x.ScheduledFor }).IsUnique();
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DnaCategory).WithMany().HasForeignKey(x => x.DnaCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Schedule).WithMany(x => x.GeneratedActivities)
                .HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<WorkActivitySchedule>(e =>
        {
            e.ToTable("WorkActivitySchedules");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(255).IsRequired();
            e.Property(x => x.Description).HasMaxLength(8000);
            e.Property(x => x.TargetMode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Frequency).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Weekdays).HasMaxLength(100);
            e.Property(x => x.BusinessImpact).HasMaxLength(4000);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.HasIndex(x => new { x.TenantId, x.IsActive });
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DnaCategory).WithMany().HasForeignKey(x => x.DnaCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<WorkCalendarDay>(e =>
        {
            e.ToTable("WorkCalendarDays");
            e.HasKey(x => x.Id);
            e.Property(x => x.DayType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Label).HasMaxLength(120);
            e.HasIndex(x => new { x.TenantId, x.Date }).IsUnique();
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<WorkLeave>(e =>
        {
            e.ToTable("WorkLeaves");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.Date }).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<DeploymentManifestNonce>(e =>
        {
            e.ToTable("DeploymentManifestNonces");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nonce).HasMaxLength(128).IsRequired();
            e.Property(x => x.DeploymentId).IsRequired();
            e.HasIndex(x => x.Nonce).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Used, x.ExpiresAt });
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<DeploymentPackage>(e =>
        {
            e.ToTable("DeploymentPackages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Version).HasMaxLength(100).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Name, x.Version }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Sha256 });
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<DeploymentRequest>(e =>
        {
            e.ToTable("DeploymentRequests");
            e.HasKey(x => x.Id);
            e.Property(x => x.PackageName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PackageVersion).HasMaxLength(100).IsRequired();
            e.Property(x => x.PackageSha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.Environment).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.RequestedBySubject).HasMaxLength(255).IsRequired();
            e.Property(x => x.RequestedByDisplay).HasMaxLength(255).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.ChangeTicketId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.TargetAgentId, x.Status });
            e.HasOne(x => x.TargetAgent).WithMany().HasForeignKey(x => x.TargetAgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.DeploymentPackageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ChangeTicket).WithMany().HasForeignKey(x => x.ChangeTicketId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.ToTable("AuditEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorType).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ActorDisplay).HasMaxLength(200);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.TargetType).HasMaxLength(100).IsRequired();
            e.Property(x => x.TargetId).HasMaxLength(100);
            e.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
            e.Property(x => x.DetailsJson).HasColumnType("text");
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<UsageEvent>(e =>
        {
            e.ToTable("UsageEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Kind, x.OccurredAt });
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        if (Database.IsSqlite())
        {
            var dateConverter = new DateTimeOffsetToBinaryConverter();
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(dateConverter);
                }
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }

                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified
                && entry.Entity is ITenantOwned tenantOwned
                && !_tenantContext.IsSystem)
            {
                if (tenantOwned.TenantId == Guid.Empty)
                {
                    throw new InvalidOperationException("Tenant-owned entity is missing a tenant id.");
                }

                if (_tenantContext.TenantId != tenantOwned.TenantId)
                {
                    throw new InvalidOperationException("Cross-tenant write rejected by persistence guard.");
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
