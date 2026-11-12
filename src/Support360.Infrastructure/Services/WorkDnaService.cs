using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Contracts.WorkDna;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class WorkDnaService : IWorkDnaService
{
    private const double HoursPerWorkingDay = 8.0;

    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IValidator<CreateProjectRequest> _createProject;
    private readonly IValidator<CreateWorkDnaCategoryRequest> _createCategory;
    private readonly IValidator<CreateWorkActivityRequest> _createActivity;
    private readonly IValidator<UpdateWorkActivityRequest> _updateActivity;
    private readonly IValidator<CreateWorkActivityScheduleRequest> _createSchedule;
    private readonly IValidator<UpdateWorkActivityScheduleRequest> _updateSchedule;
    private readonly IValidator<SetWorkCalendarDayRequest> _setCalendarDay;
    private readonly IValidator<CreateWorkLeaveRequest> _createLeave;

    public WorkDnaService(
        ISupport360DbContext db,
        ITenantContext tenant,
        IClock clock,
        IAuditService audit,
        IValidator<CreateProjectRequest> createProject,
        IValidator<CreateWorkDnaCategoryRequest> createCategory,
        IValidator<CreateWorkActivityRequest> createActivity,
        IValidator<UpdateWorkActivityRequest> updateActivity,
        IValidator<CreateWorkActivityScheduleRequest> createSchedule,
        IValidator<UpdateWorkActivityScheduleRequest> updateSchedule,
        IValidator<SetWorkCalendarDayRequest> setCalendarDay,
        IValidator<CreateWorkLeaveRequest> createLeave)
    {
        _db = db;
        _tenant = tenant;
        _clock = clock;
        _audit = audit;
        _createProject = createProject;
        _createCategory = createCategory;
        _createActivity = createActivity;
        _updateActivity = updateActivity;
        _createSchedule = createSchedule;
        _updateSchedule = updateSchedule;
        _setCalendarDay = setCalendarDay;
        _createLeave = createLeave;
    }

    public async Task<WorkDnaLookupsDto> GetLookupsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var organization = await GetOrganizationNameAsync(tenantId, cancellationToken);
        var projects = await _db.Projects.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.Name)
            .Select(p => new ProjectDto(p.Id, p.Name, p.Status))
            .ToListAsync(cancellationToken);
        var categories = await _db.WorkDnaCategories.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IsActive)
            .OrderBy(c => c.CategoryName).ThenBy(c => c.ActivityName)
            .Select(c => new WorkDnaCategoryDto(c.Id, c.CategoryName, c.ActivityName))
            .ToListAsync(cancellationToken);
        var team = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .OrderBy(u => u.DisplayName)
            .Select(u => new WorkDnaTeamMemberDto(u.Id, u.DisplayName, u.Email, u.Role.ToString()))
            .ToListAsync(cancellationToken);
        return new WorkDnaLookupsDto(organization, projects, categories, team);
    }

    public async Task<ProjectDto> CreateProjectAsync(RequestActor actor, CreateProjectRequest request, CancellationToken cancellationToken)
    {
        await _createProject.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var name = request.Name.Trim();
        if (await _db.Projects.AnyAsync(p => p.TenantId == actor.TenantId && p.Name == name, cancellationToken))
        {
            throw new ConflictException("A project with this name already exists.");
        }

        var project = new Project { TenantId = actor.TenantId, Name = name, Status = "Active" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.project_created", "Project", project.Id.ToString(), "success",
            new { project.Name }, actor.IpAddress, cancellationToken);
        return new ProjectDto(project.Id, project.Name, project.Status);
    }

    public async Task<WorkDnaCategoryDto> CreateCategoryAsync(RequestActor actor, CreateWorkDnaCategoryRequest request, CancellationToken cancellationToken)
    {
        await _createCategory.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var categoryName = request.CategoryName.Trim();
        var activityName = request.ActivityName.Trim();
        if (await _db.WorkDnaCategories.AnyAsync(c => c.TenantId == actor.TenantId
                && c.CategoryName == categoryName && c.ActivityName == activityName, cancellationToken))
        {
            throw new ConflictException("This Work DNA activity already exists.");
        }

        var category = new WorkDnaCategory
        {
            TenantId = actor.TenantId,
            CategoryName = categoryName,
            ActivityName = activityName
        };
        _db.WorkDnaCategories.Add(category);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.category_created", "WorkDnaCategory", category.Id.ToString(), "success",
            new { category.CategoryName, category.ActivityName }, actor.IpAddress, cancellationToken);
        return new WorkDnaCategoryDto(category.Id, category.CategoryName, category.ActivityName);
    }

    public async Task<IReadOnlyList<WorkActivityDto>> ListActivitiesAsync(
        Guid tenantId, string? month, Guid? assigneeId, string? status, Guid? projectId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var range = ParseMonth(month);
        var now = _clock.UtcNow;
        var organization = await GetOrganizationNameAsync(tenantId, cancellationToken);
        var query = _db.WorkActivities.AsNoTracking()
            .Include(a => a.Project)
            .Include(a => a.Assignee)
            .Include(a => a.DnaCategory)
            .Where(a => a.TenantId == tenantId && a.StartTime >= range.StartOffset && a.StartTime < range.EndOffset);
        if (assigneeId is Guid assignee)
        {
            query = query.Where(a => a.AssigneeId == assignee);
        }

        if (projectId is Guid project)
        {
            query = query.Where(a => a.ProjectId == project);
        }

        var activities = await query.OrderBy(a => a.StartTime).Take(2000).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<WorkActivityStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
            {
                throw new ValidationException("Unknown Work DNA activity status.");
            }

            activities = activities.Where(a => EffectiveStatus(a, now) == parsed).ToList();
        }

        var calendar = await LoadCalendarAsync(tenantId, range, cancellationToken);
        return activities.Select(a => MapActivity(a, organization, now, IsSuppressed(a, calendar))).ToArray();
    }

    public async Task<WorkActivityDto> CreateActivityAsync(RequestActor actor, CreateWorkActivityRequest request, CancellationToken cancellationToken)
    {
        await _createActivity.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        await EnsureReferencesAsync(actor.TenantId, request.ProjectId, request.AssigneeId, request.DnaCategoryId, cancellationToken);
        var now = _clock.UtcNow;
        var activity = new WorkActivity
        {
            TenantId = actor.TenantId,
            ProjectId = request.ProjectId,
            AssigneeId = request.AssigneeId,
            DnaCategoryId = request.DnaCategoryId,
            Title = request.Title.Trim(),
            Description = TrimOrNull(request.Description),
            TargetMode = Enum.Parse<WorkDnaTargetMode>(request.TargetMode, true),
            EffortMinutes = request.EffortMinutes,
            Severity = Enum.Parse<WorkDnaSeverity>(request.Severity, true),
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IgnoreOff = request.IgnoreOff,
            BusinessImpact = request.BusinessImpact.Trim(),
            Status = request.StartTime > now ? WorkActivityStatus.Scheduled : WorkActivityStatus.Pending
        };
        _db.WorkActivities.Add(activity);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.activity_logged", "WorkActivity", activity.Id.ToString(), "success",
            new { activity.Title, activity.EffortMinutes, activity.Severity }, actor.IpAddress, cancellationToken);
        await LoadReferencesAsync(activity, cancellationToken);
        return MapActivity(
            activity,
            await GetOrganizationNameAsync(actor.TenantId, cancellationToken),
            now,
            await IsSuppressedAsync(activity, cancellationToken));
    }

    public async Task<WorkActivityDto> AutoCaptureActivityAsync(RequestActor actor, AutoCaptureWorkActivityRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Source))
        {
            throw new ValidationException("Auto-capture source is required.");
        }

        var source = request.Source.Trim();
        var marker = string.IsNullOrWhiteSpace(request.SourceReference)
            ? null
            : $"[auto-capture:{source}:{request.SourceReference.Trim()}]";
        if (marker is not null)
        {
            _tenant.SetTenant(actor.TenantId);
            var existing = await _db.WorkActivities.AsNoTracking()
                .Include(a => a.Project)
                .Include(a => a.Assignee)
                .Include(a => a.DnaCategory)
                .SingleOrDefaultAsync(
                    a => a.TenantId == actor.TenantId && a.Description != null && a.Description.Contains(marker),
                    cancellationToken);
            if (existing is not null)
            {
                return MapActivity(
                    existing,
                    await GetOrganizationNameAsync(actor.TenantId, cancellationToken),
                    _clock.UtcNow,
                    await IsSuppressedAsync(existing, cancellationToken));
            }
        }

        var start = request.StartTime ?? _clock.UtcNow;
        var description = string.Join(
            " | ",
            new[] { TrimOrNull(request.Description), marker }.Where(value => value is not null));
        var createRequest = new CreateWorkActivityRequest(
            request.ProjectId,
            request.AssigneeId,
            request.DnaCategoryId,
            request.Title,
            description,
            "Auto",
            request.EffortMinutes,
            request.Severity,
            start,
            start.AddMinutes(request.EffortMinutes),
            true,
            request.BusinessImpact);
        var activity = await CreateActivityAsync(actor, createRequest, cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.diagnostic_auto_captured", "WorkActivity", activity.Id.ToString(), "success",
            new { Source = source, request.SourceReference }, actor.IpAddress, cancellationToken);
        return activity;
    }

    public async Task<WorkActivityDto?> UpdateActivityAsync(RequestActor actor, Guid activityId, UpdateWorkActivityRequest request, CancellationToken cancellationToken)
    {
        await _updateActivity.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var activity = await _db.WorkActivities
            .Include(a => a.Project)
            .Include(a => a.Assignee)
            .Include(a => a.DnaCategory)
            .SingleOrDefaultAsync(a => a.TenantId == actor.TenantId && a.Id == activityId, cancellationToken);
        if (activity is null)
        {
            return null;
        }

        await EnsureReferencesAsync(actor.TenantId, request.ProjectId, request.AssigneeId, request.DnaCategoryId, cancellationToken);
        activity.ProjectId = request.ProjectId;
        activity.AssigneeId = request.AssigneeId;
        activity.DnaCategoryId = request.DnaCategoryId;
        activity.Title = request.Title.Trim();
        activity.Description = TrimOrNull(request.Description);
        activity.TargetMode = Enum.Parse<WorkDnaTargetMode>(request.TargetMode, true);
        activity.EffortMinutes = request.EffortMinutes;
        activity.Severity = Enum.Parse<WorkDnaSeverity>(request.Severity, true);
        activity.StartTime = request.StartTime;
        activity.EndTime = request.EndTime;
        activity.IgnoreOff = request.IgnoreOff;
        activity.BusinessImpact = request.BusinessImpact.Trim();
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var next = Enum.Parse<WorkActivityStatus>(request.Status, true);
            activity.Status = next;
            activity.CompletedAt = next == WorkActivityStatus.Completed ? _clock.UtcNow : null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.activity_updated", "WorkActivity", activity.Id.ToString(), "success",
            new { activity.Title, activity.Status }, actor.IpAddress, cancellationToken);
        return MapActivity(
            activity,
            await GetOrganizationNameAsync(actor.TenantId, cancellationToken),
            _clock.UtcNow,
            await IsSuppressedAsync(activity, cancellationToken));
    }

    public async Task<WorkActivityDto?> CompleteActivityAsync(RequestActor actor, Guid activityId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(actor.TenantId);
        var activity = await _db.WorkActivities
            .Include(a => a.Project)
            .Include(a => a.Assignee)
            .Include(a => a.DnaCategory)
            .SingleOrDefaultAsync(a => a.TenantId == actor.TenantId && a.Id == activityId, cancellationToken);
        if (activity is null)
        {
            return null;
        }

        if (activity.Status == WorkActivityStatus.Completed)
        {
            return MapActivity(
                activity,
                await GetOrganizationNameAsync(actor.TenantId, cancellationToken),
                _clock.UtcNow,
                await IsSuppressedAsync(activity, cancellationToken));
        }

        activity.Status = WorkActivityStatus.Completed;
        activity.CompletedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.activity_completed", "WorkActivity", activity.Id.ToString(), "success",
            new { activity.Title, activity.EffortMinutes }, actor.IpAddress, cancellationToken);
        return MapActivity(
            activity,
            await GetOrganizationNameAsync(actor.TenantId, cancellationToken),
            _clock.UtcNow,
            await IsSuppressedAsync(activity, cancellationToken));
    }

    public async Task<WorkCapacityDto> GetCapacityAsync(Guid tenantId, string? month, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var range = ParseMonth(month);
        var activities = await LoadMonthActivitiesAsync(tenantId, range, cancellationToken);
        var calendar = await LoadCalendarAsync(tenantId, range, cancellationToken);
        var leaves = await _db.WorkLeaves.AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.Date >= range.Start && l.Date < range.End)
            .Select(l => l.Date)
            .ToListAsync(cancellationToken);
        var leaveDates = leaves.ToHashSet();
        var now = _clock.UtcNow;
        var days = BuildDays(range, calendar, activities, leaveDates, now);
        var totalWorkingDays = days.Count(d => !d.IsWeekend) + days.Count(d => d.IsWeekend && d.IsExplicitWorking);
        var holidayDays = days.Count(d => !d.IsWeekend && !d.IsWorking);
        var leaveDays = days.Count(d => d.IsWorking && d.HasLeave);
        var capacityDays = Math.Max(0, totalWorkingDays - holidayDays - leaveDays);
        var workingCapacityHours = capacityDays * HoursPerWorkingDay;
        var bookedHours = Math.Round(days.Sum(d => d.BookedMinutes) / 60.0, 2);
        var pendingActivities = days.Sum(d => d.Pending + d.Scheduled);
        var availableHours = Math.Round(Math.Max(0, workingCapacityHours - bookedHours), 2);
        var utilization = workingCapacityHours > 0
            ? Math.Round(bookedHours / workingCapacityHours * 100, 2)
            : 0;

        var timeline = days
            .Take(30)
            .Select(d => new WorkPulsePointDto(
                d.Date,
                Math.Round(d.BookedMinutes / 60.0, 2),
                d.IsWorking ? HoursPerWorkingDay : 0,
                d.Pending + d.Scheduled))
            .ToArray();

        return new WorkCapacityDto(
            range.Label,
            totalWorkingDays,
            holidayDays,
            leaveDays,
            workingCapacityHours,
            bookedHours,
            availableHours,
            utilization,
            pendingActivities,
            days.Select(d => new WorkCalendarDayDto(d.Date, !d.IsWorking, d.OffLabel, d.Total, d.Completed, d.Pending, d.Scheduled, d.Suppressed)).ToArray(),
            timeline);
    }

    public async Task<WorkCapacityRangeDto> GetCapacityRangeAsync(Guid tenantId, string? from, string? to, CancellationToken cancellationToken)
    {
        var start = ParseMonth(from).Start;
        var end = ParseMonth(to ?? from).Start;
        if (end < start)
        {
            throw new ValidationException("Capacity range end must be on or after the start month.");
        }

        var monthCount = ((end.Year - start.Year) * 12) + end.Month - start.Month + 1;
        if (monthCount > 12)
        {
            throw new ValidationException("Capacity range cannot exceed 12 months.");
        }

        var months = new List<WorkCapacityDto>(monthCount);
        for (var month = start; month <= end; month = month.AddMonths(1))
        {
            months.Add(await GetCapacityAsync(tenantId, month.ToString("yyyy-MM", CultureInfo.InvariantCulture), cancellationToken));
        }

        return new WorkCapacityRangeDto(months[0].Month, months[^1].Month, months);
    }

    public async Task<IReadOnlyList<WorkCalendarDayDto>> GetCalendarAsync(Guid tenantId, string? month, CancellationToken cancellationToken)
    {
        var capacity = await GetCapacityAsync(tenantId, month, cancellationToken);
        return capacity.Days;
    }

    public async Task<IReadOnlyList<WorkActivityScheduleDto>> ListSchedulesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var schedules = await _db.WorkActivitySchedules.AsNoTracking()
            .Include(s => s.Project)
            .Include(s => s.Assignee)
            .Include(s => s.DnaCategory)
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.Title)
            .ToListAsync(cancellationToken);
        return schedules.Select(MapSchedule).ToArray();
    }

    public async Task<WorkActivityScheduleDto> CreateScheduleAsync(RequestActor actor, CreateWorkActivityScheduleRequest request, CancellationToken cancellationToken)
    {
        await _createSchedule.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        await EnsureReferencesAsync(actor.TenantId, request.ProjectId, request.AssigneeId, request.DnaCategoryId, cancellationToken);
        var schedule = new WorkActivitySchedule
        {
            TenantId = actor.TenantId,
            ProjectId = request.ProjectId,
            AssigneeId = request.AssigneeId,
            DnaCategoryId = request.DnaCategoryId,
            Title = request.Title.Trim(),
            Description = TrimOrNull(request.Description),
            TargetMode = Enum.Parse<WorkDnaTargetMode>(request.TargetMode, true),
            EffortMinutes = request.EffortMinutes,
            Severity = Enum.Parse<WorkDnaSeverity>(request.Severity, true),
            IgnoreOff = request.IgnoreOff,
            BusinessImpact = request.BusinessImpact.Trim(),
            Frequency = Enum.Parse<WorkScheduleFrequency>(request.Frequency, true),
            Weekdays = NormalizeWeekdays(request.Weekdays),
            DayOfMonth = request.DayOfMonth,
            StartHourUtc = request.StartHourUtc,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = true
        };
        _db.WorkActivitySchedules.Add(schedule);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.schedule_created", "WorkActivitySchedule", schedule.Id.ToString(), "success",
            new { schedule.Title, schedule.Frequency }, actor.IpAddress, cancellationToken);
        await LoadReferencesAsync(schedule, cancellationToken);
        return MapSchedule(schedule);
    }

    public async Task<WorkActivityScheduleDto?> UpdateScheduleAsync(RequestActor actor, Guid scheduleId, UpdateWorkActivityScheduleRequest request, CancellationToken cancellationToken)
    {
        await _updateSchedule.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var schedule = await _db.WorkActivitySchedules
            .Include(s => s.Project)
            .Include(s => s.Assignee)
            .Include(s => s.DnaCategory)
            .SingleOrDefaultAsync(s => s.TenantId == actor.TenantId && s.Id == scheduleId, cancellationToken);
        if (schedule is null)
        {
            return null;
        }

        await EnsureReferencesAsync(actor.TenantId, request.ProjectId, request.AssigneeId, request.DnaCategoryId, cancellationToken);
        schedule.ProjectId = request.ProjectId;
        schedule.AssigneeId = request.AssigneeId;
        schedule.DnaCategoryId = request.DnaCategoryId;
        schedule.Title = request.Title.Trim();
        schedule.Description = TrimOrNull(request.Description);
        schedule.TargetMode = Enum.Parse<WorkDnaTargetMode>(request.TargetMode, true);
        schedule.EffortMinutes = request.EffortMinutes;
        schedule.Severity = Enum.Parse<WorkDnaSeverity>(request.Severity, true);
        schedule.IgnoreOff = request.IgnoreOff;
        schedule.BusinessImpact = request.BusinessImpact.Trim();
        schedule.Frequency = Enum.Parse<WorkScheduleFrequency>(request.Frequency, true);
        schedule.Weekdays = NormalizeWeekdays(request.Weekdays);
        schedule.DayOfMonth = request.DayOfMonth;
        schedule.StartHourUtc = request.StartHourUtc;
        schedule.StartDate = request.StartDate;
        schedule.EndDate = request.EndDate;
        schedule.IsActive = request.IsActive;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.schedule_updated", "WorkActivitySchedule", schedule.Id.ToString(), "success",
            new { schedule.Title, schedule.IsActive }, actor.IpAddress, cancellationToken);
        return MapSchedule(schedule);
    }

    public async Task<MaterializeSchedulesResponse> MaterializeSchedulesAsync(RequestActor actor, string? month, Guid? scheduleId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(actor.TenantId);
        var range = ParseMonth(month);
        var schedules = await _db.WorkActivitySchedules
            .Where(s => s.TenantId == actor.TenantId && s.IsActive && (scheduleId == null || s.Id == scheduleId))
            .ToListAsync(cancellationToken);
        if (scheduleId is not null && schedules.Count == 0)
        {
            throw new NotFoundException("Work DNA schedule was not found in this tenant.");
        }

        var calendar = await LoadCalendarAsync(actor.TenantId, range, cancellationToken);
        var existing = await LoadExistingScheduleKeysAsync(actor.TenantId, range, cancellationToken);
        var now = _clock.UtcNow;
        var generated = 0;
        var skipped = 0;
        foreach (var schedule in schedules)
        {
            var result = Materialize(schedule, range, calendar, existing, now);
            generated += result.Generated;
            skipped += result.Skipped;
        }

        if (generated > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.schedules_materialized", "WorkActivitySchedule", scheduleId?.ToString(), "success",
            new { range.Label, Generated = generated, Skipped = skipped }, actor.IpAddress, cancellationToken);
        return new MaterializeSchedulesResponse(range.Label, generated, skipped);
    }

    public async Task<int> MaterializeAllTenantsAsync(DateOnly month, CancellationToken cancellationToken)
    {
        _tenant.SetSystem();
        var range = MonthRange.From(month, _clock.UtcNow);
        var schedules = await _db.WorkActivitySchedules.IgnoreQueryFilters()
            .Where(s => s.IsActive)
            .ToListAsync(cancellationToken);
        if (schedules.Count == 0)
        {
            return 0;
        }

        var calendarDays = await _db.WorkCalendarDays.IgnoreQueryFilters()
            .Where(d => d.Date >= range.Start && d.Date < range.End)
            .ToListAsync(cancellationToken);
        var calendars = calendarDays.GroupBy(d => d.TenantId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<DateOnly, WorkCalendarDay>)g.ToDictionary(d => d.Date));
        var existingActivities = await _db.WorkActivities.IgnoreQueryFilters()
            .Where(a => a.ScheduleId != null && a.StartTime >= range.StartOffset && a.StartTime < range.EndOffset)
            .Select(a => new { a.TenantId, a.ScheduleId, a.ScheduledFor })
            .ToListAsync(cancellationToken);
        var existing = existingActivities
            .Where(a => a.ScheduleId is not null && a.ScheduledFor is not null)
            .Select(a => (a.ScheduleId!.Value, a.ScheduledFor!.Value))
            .ToHashSet();
        var now = _clock.UtcNow;
        var generated = 0;
        foreach (var schedule in schedules)
        {
            var calendar = calendars.TryGetValue(schedule.TenantId, out var tenantCalendar)
                ? tenantCalendar
                : new Dictionary<DateOnly, WorkCalendarDay>();
            generated += Materialize(schedule, range, calendar, existing, now).Generated;
        }

        if (generated > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return generated;
    }

    public async Task<IReadOnlyList<WorkCalendarDayEntryDto>> ListCalendarDaysAsync(Guid tenantId, string? month, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var range = ParseMonth(month);
        var days = await _db.WorkCalendarDays.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Date >= range.Start && d.Date < range.End)
            .OrderBy(d => d.Date)
            .ToListAsync(cancellationToken);
        return days.Select(d => new WorkCalendarDayEntryDto(d.Date, d.DayType.ToString(), d.Label)).ToArray();
    }

    public async Task<WorkCalendarDayEntryDto> SetCalendarDayAsync(RequestActor actor, SetWorkCalendarDayRequest request, CancellationToken cancellationToken)
    {
        await _setCalendarDay.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var dayType = Enum.Parse<WorkCalendarDayType>(request.DayType, true);
        var day = await _db.WorkCalendarDays
            .SingleOrDefaultAsync(d => d.TenantId == actor.TenantId && d.Date == request.Date, cancellationToken);
        if (day is null)
        {
            day = new WorkCalendarDay { TenantId = actor.TenantId, Date = request.Date };
            _db.WorkCalendarDays.Add(day);
        }

        day.DayType = dayType;
        day.Label = TrimOrNull(request.Label);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.calendar_day_set", "WorkCalendarDay", day.Id.ToString(), "success",
            new { day.Date, DayType = dayType.ToString(), day.Label }, actor.IpAddress, cancellationToken);
        return new WorkCalendarDayEntryDto(day.Date, day.DayType.ToString(), day.Label);
    }

    public async Task<bool> RemoveCalendarDayAsync(RequestActor actor, DateOnly date, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(actor.TenantId);
        var day = await _db.WorkCalendarDays
            .SingleOrDefaultAsync(d => d.TenantId == actor.TenantId && d.Date == date, cancellationToken);
        if (day is null)
        {
            return false;
        }

        _db.WorkCalendarDays.Remove(day);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.calendar_day_removed", "WorkCalendarDay", day.Id.ToString(), "success",
            new { day.Date }, actor.IpAddress, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<WorkLeaveDto>> ListLeavesAsync(Guid tenantId, string? month, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var range = ParseMonth(month);
        var leaves = await _db.WorkLeaves.AsNoTracking()
            .Include(l => l.User)
            .Where(l => l.TenantId == tenantId && l.Date >= range.Start && l.Date < range.End)
            .OrderBy(l => l.Date)
            .ToListAsync(cancellationToken);
        return leaves.Select(l => new WorkLeaveDto(l.Id, l.UserId, l.User.DisplayName, l.Date, l.Hours, l.Reason)).ToArray();
    }

    public async Task<WorkLeaveDto> CreateLeaveAsync(RequestActor actor, CreateWorkLeaveRequest request, CancellationToken cancellationToken)
    {
        await _createLeave.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var user = await _db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.TenantId == actor.TenantId && u.Id == request.UserId && u.IsActive, cancellationToken)
            ?? throw new NotFoundException("Team member was not found in this tenant.");
        if (await _db.WorkLeaves.AnyAsync(l => l.TenantId == actor.TenantId
                && l.UserId == request.UserId && l.Date == request.Date, cancellationToken))
        {
            throw new ConflictException("A leave entry already exists for this team member on that date.");
        }

        var leave = new WorkLeave
        {
            TenantId = actor.TenantId,
            UserId = request.UserId,
            Date = request.Date,
            Hours = request.Hours,
            Reason = TrimOrNull(request.Reason)
        };
        _db.WorkLeaves.Add(leave);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "work_dna.leave_created", "WorkLeave", leave.Id.ToString(), "success",
            new { leave.UserId, leave.Date, leave.Hours }, actor.IpAddress, cancellationToken);
        return new WorkLeaveDto(leave.Id, leave.UserId, user.DisplayName, leave.Date, leave.Hours, leave.Reason);
    }

    private async Task<List<WorkActivity>> LoadMonthActivitiesAsync(Guid tenantId, MonthRange range, CancellationToken cancellationToken) =>
        await _db.WorkActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.StartTime >= range.StartOffset && a.StartTime < range.EndOffset)
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<DateOnly, WorkCalendarDay>> LoadCalendarAsync(Guid tenantId, MonthRange range, CancellationToken cancellationToken) =>
        (await _db.WorkCalendarDays.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Date >= range.Start && d.Date < range.End)
            .ToListAsync(cancellationToken))
        .ToDictionary(d => d.Date);

    private async Task<HashSet<(Guid ScheduleId, DateOnly Date)>> LoadExistingScheduleKeysAsync(Guid tenantId, MonthRange range, CancellationToken cancellationToken)
    {
        var rows = await _db.WorkActivities.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.ScheduleId != null
                && a.StartTime >= range.StartOffset && a.StartTime < range.EndOffset)
            .Select(a => new { a.ScheduleId, a.ScheduledFor })
            .ToListAsync(cancellationToken);
        return rows
            .Where(a => a.ScheduleId is not null && a.ScheduledFor is not null)
            .Select(a => (a.ScheduleId!.Value, a.ScheduledFor!.Value))
            .ToHashSet();
    }

    private (int Generated, int Skipped) Materialize(
        WorkActivitySchedule schedule,
        MonthRange range,
        IReadOnlyDictionary<DateOnly, WorkCalendarDay> calendar,
        HashSet<(Guid ScheduleId, DateOnly Date)> existing,
        DateTimeOffset now)
    {
        var start = schedule.StartDate > range.Start ? schedule.StartDate : range.Start;
        var end = schedule.EndDate < range.EndInclusive ? schedule.EndDate : range.EndInclusive;
        if (start > end)
        {
            return (0, 0);
        }

        var generated = 0;
        var skipped = 0;
        var weekdays = ParseWeekdays(schedule.Weekdays);
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (!MatchesFrequency(schedule, date, weekdays))
            {
                continue;
            }

            if (!schedule.IgnoreOff && !IsWorkingDay(date, calendar))
            {
                continue;
            }

            if (!existing.Add((schedule.Id, date)))
            {
                skipped++;
                continue;
            }

            var startTime = new DateTimeOffset(date.Year, date.Month, date.Day, schedule.StartHourUtc, 0, 0, TimeSpan.Zero);
            _db.WorkActivities.Add(new WorkActivity
            {
                TenantId = schedule.TenantId,
                ProjectId = schedule.ProjectId,
                AssigneeId = schedule.AssigneeId,
                DnaCategoryId = schedule.DnaCategoryId,
                ScheduleId = schedule.Id,
                ScheduledFor = date,
                Title = schedule.Title,
                Description = schedule.Description,
                TargetMode = schedule.TargetMode,
                EffortMinutes = schedule.EffortMinutes,
                Severity = schedule.Severity,
                StartTime = startTime,
                EndTime = startTime.AddMinutes(schedule.EffortMinutes),
                IgnoreOff = schedule.IgnoreOff,
                BusinessImpact = schedule.BusinessImpact,
                Status = startTime > now ? WorkActivityStatus.Scheduled : WorkActivityStatus.Pending
            });
            generated++;
        }

        return (generated, skipped);
    }

    private static bool MatchesFrequency(WorkActivitySchedule schedule, DateOnly date, HashSet<DayOfWeek> weekdays) =>
        schedule.Frequency switch
        {
            WorkScheduleFrequency.Daily => true,
            WorkScheduleFrequency.Weekly => weekdays.Contains(date.DayOfWeek),
            WorkScheduleFrequency.Monthly => schedule.DayOfMonth is int day
                && date.Day == Math.Min(day, DateTime.DaysInMonth(date.Year, date.Month)),
            _ => false
        };

    private static bool IsWorkingDay(DateOnly date, IReadOnlyDictionary<DateOnly, WorkCalendarDay> calendar)
    {
        if (calendar.TryGetValue(date, out var day))
        {
            return day.DayType == WorkCalendarDayType.Working;
        }

        return date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    private static bool IsSuppressed(WorkActivity activity, IReadOnlyDictionary<DateOnly, WorkCalendarDay> calendar) =>
        !activity.IgnoreOff && !IsWorkingDay(DateOnly.FromDateTime(activity.StartTime.UtcDateTime), calendar);

    private async Task<bool> IsSuppressedAsync(WorkActivity activity, CancellationToken cancellationToken)
    {
        if (activity.IgnoreOff)
        {
            return false;
        }

        var date = DateOnly.FromDateTime(activity.StartTime.UtcDateTime);
        var day = await _db.WorkCalendarDays.AsNoTracking()
            .SingleOrDefaultAsync(d => d.TenantId == activity.TenantId && d.Date == date, cancellationToken);
        if (day is not null)
        {
            return day.DayType != WorkCalendarDayType.Working;
        }

        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    }

    private static List<DayBucket> BuildDays(
        MonthRange range,
        IReadOnlyDictionary<DateOnly, WorkCalendarDay> calendar,
        IReadOnlyList<WorkActivity> activities,
        HashSet<DateOnly> leaveDates,
        DateTimeOffset now)
    {
        var byDate = activities
            .GroupBy(a => DateOnly.FromDateTime(a.StartTime.UtcDateTime))
            .ToDictionary(g => g.Key, g => g.ToList());
        var buckets = new List<DayBucket>();
        for (var date = range.Start; date < range.End; date = date.AddDays(1))
        {
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            calendar.TryGetValue(date, out var overrideDay);
            var isExplicitWorking = overrideDay?.DayType == WorkCalendarDayType.Working;
            var isWorking = isExplicitWorking
                || (!isWeekend && overrideDay?.DayType is not (WorkCalendarDayType.Holiday or WorkCalendarDayType.Off));
            var offLabel = isWorking
                ? null
                : overrideDay?.Label
                  ?? (overrideDay?.DayType == WorkCalendarDayType.Holiday ? "Holiday"
                      : overrideDay?.DayType == WorkCalendarDayType.Off ? "OFF"
                      : "Weekend");
            byDate.TryGetValue(date, out var dayActivities);
            dayActivities ??= [];
            var counted = dayActivities.Where(a => a.IgnoreOff || isWorking).ToList();
            var suppressed = dayActivities.Count - counted.Count;
            var completed = counted.Count(a => a.Status == WorkActivityStatus.Completed);
            var scheduled = counted.Count(a => EffectiveStatus(a, now) == WorkActivityStatus.Scheduled);
            var pending = counted.Count(a => EffectiveStatus(a, now) is WorkActivityStatus.Pending or WorkActivityStatus.Overdue);
            buckets.Add(new DayBucket(
                date,
                isWeekend,
                isExplicitWorking,
                isWorking,
                offLabel,
                counted.Count,
                completed,
                pending,
                scheduled,
                counted.Sum(a => a.EffortMinutes),
                suppressed,
                leaveDates.Contains(date)));
        }

        return buckets;
    }

    private async Task EnsureReferencesAsync(Guid tenantId, Guid projectId, Guid assigneeId, Guid dnaCategoryId, CancellationToken cancellationToken)
    {
        if (!await _db.Projects.AnyAsync(p => p.TenantId == tenantId && p.Id == projectId, cancellationToken))
        {
            throw new NotFoundException("Project was not found in this tenant.");
        }

        if (!await _db.Users.AnyAsync(u => u.TenantId == tenantId && u.Id == assigneeId && u.IsActive, cancellationToken))
        {
            throw new NotFoundException("Assignee was not found in this tenant.");
        }

        if (!await _db.WorkDnaCategories.AnyAsync(c => c.TenantId == tenantId && c.Id == dnaCategoryId && c.IsActive, cancellationToken))
        {
            throw new NotFoundException("Work DNA activity was not found in this tenant.");
        }
    }

    private async Task LoadReferencesAsync(WorkActivity activity, CancellationToken cancellationToken)
    {
        activity.Project = await _db.Projects.SingleAsync(p => p.Id == activity.ProjectId, cancellationToken);
        activity.Assignee = await _db.Users.SingleAsync(u => u.Id == activity.AssigneeId, cancellationToken);
        activity.DnaCategory = await _db.WorkDnaCategories.SingleAsync(c => c.Id == activity.DnaCategoryId, cancellationToken);
    }

    private async Task LoadReferencesAsync(WorkActivitySchedule schedule, CancellationToken cancellationToken)
    {
        schedule.Project = await _db.Projects.SingleAsync(p => p.Id == schedule.ProjectId, cancellationToken);
        schedule.Assignee = await _db.Users.SingleAsync(u => u.Id == schedule.AssigneeId, cancellationToken);
        schedule.DnaCategory = await _db.WorkDnaCategories.SingleAsync(c => c.Id == schedule.DnaCategoryId, cancellationToken);
    }

    private async Task<string> GetOrganizationNameAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? string.Empty;

    private static WorkActivityDto MapActivity(WorkActivity activity, string organization, DateTimeOffset now, bool isSuppressed) => new(
        activity.Id,
        organization,
        activity.ProjectId,
        activity.Project?.Name ?? string.Empty,
        activity.AssigneeId,
        activity.Assignee?.DisplayName ?? string.Empty,
        activity.DnaCategoryId,
        activity.DnaCategory?.CategoryName ?? string.Empty,
        activity.DnaCategory?.ActivityName ?? string.Empty,
        activity.Title,
        activity.Description,
        activity.TargetMode.ToString(),
        activity.EffortMinutes,
        FormatEffort(activity.EffortMinutes),
        activity.Severity.ToString(),
        activity.StartTime,
        activity.EndTime,
        activity.IgnoreOff,
        activity.BusinessImpact,
        EffectiveStatus(activity, now).ToString(),
        activity.CompletedAt,
        activity.ScheduleId,
        isSuppressed);

    private static WorkActivityScheduleDto MapSchedule(WorkActivitySchedule schedule) => new(
        schedule.Id,
        schedule.ProjectId,
        schedule.Project?.Name ?? string.Empty,
        schedule.AssigneeId,
        schedule.Assignee?.DisplayName ?? string.Empty,
        schedule.DnaCategoryId,
        schedule.DnaCategory?.CategoryName ?? string.Empty,
        schedule.DnaCategory?.ActivityName ?? string.Empty,
        schedule.Title,
        schedule.Description,
        schedule.TargetMode.ToString(),
        schedule.EffortMinutes,
        FormatEffort(schedule.EffortMinutes),
        schedule.Severity.ToString(),
        schedule.IgnoreOff,
        schedule.BusinessImpact,
        schedule.Frequency.ToString(),
        ParseWeekdays(schedule.Weekdays).Select(d => d.ToString()).ToArray(),
        schedule.DayOfMonth,
        schedule.StartHourUtc,
        schedule.StartDate,
        schedule.EndDate,
        schedule.IsActive);

    private static WorkActivityStatus EffectiveStatus(WorkActivity activity, DateTimeOffset now)
    {
        if (activity.Status == WorkActivityStatus.Completed)
        {
            return WorkActivityStatus.Completed;
        }

        return activity.EndTime < now ? WorkActivityStatus.Overdue : activity.Status;
    }

    private static string FormatEffort(int minutes)
    {
        var hours = minutes / 60;
        var remainder = minutes % 60;
        if (hours == 0)
        {
            return $"{remainder}m";
        }

        return remainder == 0 ? $"{hours}h" : $"{hours}h {remainder}m";
    }

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeWeekdays(IReadOnlyList<string>? weekdays)
    {
        if (weekdays is null || weekdays.Count == 0)
        {
            return null;
        }

        return string.Join(",", weekdays
            .Select(d => Enum.Parse<DayOfWeek>(d, true).ToString())
            .Distinct()
            .OrderBy(d => Enum.Parse<DayOfWeek>(d)));
    }

    private static HashSet<DayOfWeek> ParseWeekdays(string? weekdays)
    {
        if (string.IsNullOrWhiteSpace(weekdays))
        {
            return [];
        }

        return weekdays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => Enum.Parse<DayOfWeek>(d, true))
            .ToHashSet();
    }

    private static MonthRange ParseMonth(string? month)
    {
        if (string.IsNullOrWhiteSpace(month))
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return MonthRange.From(new DateOnly(today.Year, today.Month, 1), DateTimeOffset.UtcNow);
        }

        if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new ValidationException("Month must use the yyyy-MM format.");
        }

        return MonthRange.From(new DateOnly(parsed.Year, parsed.Month, 1), DateTimeOffset.UtcNow);
    }

    private sealed record DayBucket(
        DateOnly Date,
        bool IsWeekend,
        bool IsExplicitWorking,
        bool IsWorking,
        string? OffLabel,
        int Total,
        int Completed,
        int Pending,
        int Scheduled,
        int BookedMinutes,
        int Suppressed,
        bool HasLeave);

    private readonly record struct MonthRange(DateOnly Start, DateOnly End, DateTimeOffset StartOffset, DateTimeOffset EndOffset, string Label)
    {
        public DateOnly EndInclusive => End.AddDays(-1);

        public static MonthRange From(DateOnly monthStart, DateTimeOffset now)
        {
            var start = new DateOnly(monthStart.Year, monthStart.Month, 1);
            var end = start.AddMonths(1);
            return new MonthRange(
                start,
                end,
                new DateTimeOffset(start.Year, start.Month, start.Day, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(end.Year, end.Month, end.Day, 0, 0, 0, TimeSpan.Zero),
                start.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        }
    }
}
