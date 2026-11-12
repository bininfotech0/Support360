namespace Support360.Contracts.WorkDna;

public sealed record ProjectDto(Guid Id, string Name, string Status);
public sealed record CreateProjectRequest(string Name);

public sealed record WorkDnaCategoryDto(Guid Id, string CategoryName, string ActivityName);
public sealed record CreateWorkDnaCategoryRequest(string CategoryName, string ActivityName);

public sealed record WorkDnaTeamMemberDto(Guid Id, string DisplayName, string Email, string Role);

public sealed record WorkDnaLookupsDto(
    string OrganizationName,
    IReadOnlyList<ProjectDto> Projects,
    IReadOnlyList<WorkDnaCategoryDto> Categories,
    IReadOnlyList<WorkDnaTeamMemberDto> Team);

public sealed record CreateWorkActivityRequest(
    Guid ProjectId,
    Guid AssigneeId,
    Guid DnaCategoryId,
    string Title,
    string? Description,
    string TargetMode,
    int EffortMinutes,
    string Severity,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    bool IgnoreOff,
    string BusinessImpact);

public sealed record UpdateWorkActivityRequest(
    Guid ProjectId,
    Guid AssigneeId,
    Guid DnaCategoryId,
    string Title,
    string? Description,
    string TargetMode,
    int EffortMinutes,
    string Severity,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    bool IgnoreOff,
    string BusinessImpact,
    string? Status);

public sealed record WorkActivityDto(
    Guid Id,
    string Organization,
    Guid ProjectId,
    string ProjectName,
    Guid AssigneeId,
    string AssigneeName,
    Guid DnaCategoryId,
    string CategoryName,
    string ActivityName,
    string Title,
    string? Description,
    string TargetMode,
    int EffortMinutes,
    string Effort,
    string Severity,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    bool IgnoreOff,
    string? BusinessImpact,
    string Status,
    DateTimeOffset? CompletedAt,
    Guid? ScheduleId,
    bool IsSuppressed);

public sealed record WorkCalendarDayDto(
    DateOnly Date,
    bool IsOff,
    string? OffLabel,
    int Total,
    int Completed,
    int Pending,
    int Scheduled,
    int Suppressed);

public sealed record WorkPulsePointDto(DateOnly Date, double BookedHours, double CapacityHours, int Pending);

public sealed record WorkCapacityDto(
    string Month,
    int TotalWorkingDays,
    int HolidayDays,
    int LeaveDays,
    double WorkingCapacityHours,
    double BookedHours,
    double AvailableHours,
    double UtilizationPercent,
    int PendingActivities,
    IReadOnlyList<WorkCalendarDayDto> Days,
    IReadOnlyList<WorkPulsePointDto> PendencyTimeline);

public sealed record WorkCapacityRangeDto(
    string From,
    string To,
    IReadOnlyList<WorkCapacityDto> Months);

public sealed record AutoCaptureWorkActivityRequest(
    Guid ProjectId,
    Guid AssigneeId,
    Guid DnaCategoryId,
    string Title,
    string? Description,
    int EffortMinutes,
    string Severity,
    string BusinessImpact,
    string Source,
    string? SourceReference,
    DateTimeOffset? StartTime);

public sealed record CreateWorkActivityScheduleRequest(
    Guid ProjectId,
    Guid AssigneeId,
    Guid DnaCategoryId,
    string Title,
    string? Description,
    string TargetMode,
    int EffortMinutes,
    string Severity,
    bool IgnoreOff,
    string BusinessImpact,
    string Frequency,
    IReadOnlyList<string>? Weekdays,
    int? DayOfMonth,
    int StartHourUtc,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record UpdateWorkActivityScheduleRequest(
    Guid ProjectId,
    Guid AssigneeId,
    Guid DnaCategoryId,
    string Title,
    string? Description,
    string TargetMode,
    int EffortMinutes,
    string Severity,
    bool IgnoreOff,
    string BusinessImpact,
    string Frequency,
    IReadOnlyList<string>? Weekdays,
    int? DayOfMonth,
    int StartHourUtc,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive);

public sealed record WorkActivityScheduleDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    Guid AssigneeId,
    string AssigneeName,
    Guid DnaCategoryId,
    string CategoryName,
    string ActivityName,
    string Title,
    string? Description,
    string TargetMode,
    int EffortMinutes,
    string Effort,
    string Severity,
    bool IgnoreOff,
    string? BusinessImpact,
    string Frequency,
    IReadOnlyList<string> Weekdays,
    int? DayOfMonth,
    int StartHourUtc,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsActive);

public sealed record MaterializeSchedulesResponse(string Month, int Generated, int Skipped);

public sealed record SetWorkCalendarDayRequest(DateOnly Date, string DayType, string? Label);
public sealed record WorkCalendarDayEntryDto(DateOnly Date, string DayType, string? Label);

public sealed record CreateWorkLeaveRequest(Guid UserId, DateOnly Date, int Hours, string? Reason);
public sealed record WorkLeaveDto(Guid Id, Guid UserId, string UserDisplayName, DateOnly Date, int Hours, string? Reason);
