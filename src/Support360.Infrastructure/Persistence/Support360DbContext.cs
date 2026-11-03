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
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentAlert> IncidentAlerts => Set<IncidentAlert>();
    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<ChangeApproval> ChangeApprovals => Set<ChangeApproval>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<DeploymentPackage> DeploymentPackages => Set<DeploymentPackage>();
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

        modelBuilder.Entity<Incident>(e =>
        {
            e.ToTable("Incidents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(8000).IsRequired();
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status, x.Severity, x.OpenedAt });
            e.HasOne<BatchJobExecution>().WithMany().HasForeignKey(x => x.RelatedBatchJobExecutionId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<IncidentAlert>(e =>
        {
            e.ToTable("IncidentAlerts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.IncidentId, x.AlertId }).IsUnique();
            e.HasOne(x => x.Incident).WithMany(x => x.Alerts).HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Alert).WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<ChangeRequest>(e =>
        {
            e.ToTable("ChangeRequests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(8000).IsRequired();
            e.Property(x => x.Risk).HasMaxLength(4000).IsRequired();
            e.Property(x => x.TargetEnvironment).HasMaxLength(80).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status, x.PlannedStartAt });
            e.HasOne(x => x.RelatedIncident).WithMany(x => x.ChangeRequests).HasForeignKey(x => x.RelatedIncidentId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<ChangeApproval>(e =>
        {
            e.ToTable("ChangeApprovals");
            e.HasKey(x => x.Id);
            e.Property(x => x.ApproverSubject).HasMaxLength(255).IsRequired();
            e.Property(x => x.ApproverDisplay).HasMaxLength(200).IsRequired();
            e.Property(x => x.Decision).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.HasIndex(x => new { x.TenantId, x.ChangeRequestId, x.ApproverSubject }).IsUnique();
            e.HasOne(x => x.ChangeRequest).WithMany(x => x.Approvals).HasForeignKey(x => x.ChangeRequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => _tenantContext.IsSystem || x.TenantId == _tenantContext.TenantId);
        });

        modelBuilder.Entity<ServiceRequest>(e =>
        {
            e.ToTable("ServiceRequests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(8000).IsRequired();
            e.Property(x => x.Category).HasMaxLength(100).IsRequired();
            e.Property(x => x.RequestedBySubject).HasMaxLength(255).IsRequired();
            e.Property(x => x.RequestedByDisplay).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status, x.OpenedAt });
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
            e.HasIndex(x => new { x.TenantId, x.ChangeRequestId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.TargetAgentId, x.Status });
            e.HasOne(x => x.TargetAgent).WithMany().HasForeignKey(x => x.TargetAgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.DeploymentPackageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ChangeRequest).WithMany().HasForeignKey(x => x.ChangeRequestId).OnDelete(DeleteBehavior.Restrict);
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
