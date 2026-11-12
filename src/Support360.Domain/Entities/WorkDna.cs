namespace Support360.Domain.Entities;

public sealed class Project : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public string Status { get; set; } = "Active";
}

public sealed class WorkDnaCategory : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public required string CategoryName { get; set; }
    public required string ActivityName { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class WorkActivity : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid AssigneeId { get; set; }
    public User Assignee { get; set; } = null!;
    public Guid DnaCategoryId { get; set; }
    public WorkDnaCategory DnaCategory { get; set; } = null!;
    public Guid? ScheduleId { get; set; }
    public WorkActivitySchedule? Schedule { get; set; }
    public DateOnly? ScheduledFor { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public WorkDnaTargetMode TargetMode { get; set; } = WorkDnaTargetMode.Manual;
    public int EffortMinutes { get; set; }
    public WorkDnaSeverity Severity { get; set; } = WorkDnaSeverity.Low;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public bool IgnoreOff { get; set; }
    public string? BusinessImpact { get; set; }
    public WorkActivityStatus Status { get; set; } = WorkActivityStatus.Pending;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class WorkActivitySchedule : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid AssigneeId { get; set; }
    public User Assignee { get; set; } = null!;
    public Guid DnaCategoryId { get; set; }
    public WorkDnaCategory DnaCategory { get; set; } = null!;
    public required string Title { get; set; }
    public string? Description { get; set; }
    public WorkDnaTargetMode TargetMode { get; set; } = WorkDnaTargetMode.Auto;
    public int EffortMinutes { get; set; }
    public WorkDnaSeverity Severity { get; set; } = WorkDnaSeverity.Low;
    public bool IgnoreOff { get; set; }
    public string? BusinessImpact { get; set; }
    public WorkScheduleFrequency Frequency { get; set; }
    public string? Weekdays { get; set; }
    public int? DayOfMonth { get; set; }
    public int StartHourUtc { get; set; } = 9;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<WorkActivity> GeneratedActivities { get; set; } = [];
}

public sealed class WorkCalendarDay : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public DateOnly Date { get; set; }
    public WorkCalendarDayType DayType { get; set; } = WorkCalendarDayType.Holiday;
    public string? Label { get; set; }
}

public sealed class WorkLeave : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateOnly Date { get; set; }
    public int Hours { get; set; } = 8;
    public string? Reason { get; set; }
}
