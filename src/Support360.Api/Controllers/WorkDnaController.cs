using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.WorkDna;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/work-dna")]
[Authorize(Policy = "TenantUser")]
public sealed class WorkDnaController : TenantControllerBase
{
    private readonly IWorkDnaService _service;

    public WorkDnaController(IWorkDnaService service) => _service = service;

    [HttpGet("lookups")]
    public Task<WorkDnaLookupsDto> Lookups(CancellationToken cancellationToken) =>
        _service.GetLookupsAsync(RequireTenantId(), cancellationToken);

    [HttpPost("categories")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<WorkDnaCategoryDto>> CreateCategory(CreateWorkDnaCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await _service.CreateCategoryAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/work-dna/categories/{category.Id}", category);
    }

    [HttpGet("activities")]
    public Task<IReadOnlyList<WorkActivityDto>> Activities(
        [FromQuery] string? month,
        [FromQuery] Guid? assigneeId,
        [FromQuery] string? status,
        [FromQuery] Guid? projectId,
        CancellationToken cancellationToken) =>
        _service.ListActivitiesAsync(RequireTenantId(), month, assigneeId, status, projectId, cancellationToken);

    [HttpPost("activities")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<WorkActivityDto>> CreateActivity(CreateWorkActivityRequest request, CancellationToken cancellationToken)
    {
        var activity = await _service.CreateActivityAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/work-dna/activities/{activity.Id}", activity);
    }

    [HttpPost("activities/auto-capture")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<WorkActivityDto>> AutoCaptureActivity(AutoCaptureWorkActivityRequest request, CancellationToken cancellationToken)
    {
        var activity = await _service.AutoCaptureActivityAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/work-dna/activities/{activity.Id}", activity);
    }

    [HttpPut("activities/{id:guid}")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<WorkActivityDto>> UpdateActivity(Guid id, UpdateWorkActivityRequest request, CancellationToken cancellationToken)
    {
        var activity = await _service.UpdateActivityAsync(RequireActor(), id, request, cancellationToken);
        return activity is null ? NotFound() : Ok(activity);
    }

    [HttpPost("activities/{id:guid}/complete")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<WorkActivityDto>> CompleteActivity(Guid id, CancellationToken cancellationToken)
    {
        var activity = await _service.CompleteActivityAsync(RequireActor(), id, cancellationToken);
        return activity is null ? NotFound() : Ok(activity);
    }

    [HttpGet("capacity")]
    public Task<WorkCapacityDto> Capacity([FromQuery] string? month, CancellationToken cancellationToken) =>
        _service.GetCapacityAsync(RequireTenantId(), month, cancellationToken);

    [HttpGet("capacity/range")]
    public Task<WorkCapacityRangeDto> CapacityRange(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken) =>
        _service.GetCapacityRangeAsync(RequireTenantId(), from, to, cancellationToken);

    [HttpGet("calendar")]
    public Task<IReadOnlyList<WorkCalendarDayDto>> Calendar([FromQuery] string? month, CancellationToken cancellationToken) =>
        _service.GetCalendarAsync(RequireTenantId(), month, cancellationToken);

    [HttpGet("schedules")]
    public Task<IReadOnlyList<WorkActivityScheduleDto>> Schedules(CancellationToken cancellationToken) =>
        _service.ListSchedulesAsync(RequireTenantId(), cancellationToken);

    [HttpPost("schedules")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<WorkActivityScheduleDto>> CreateSchedule(CreateWorkActivityScheduleRequest request, CancellationToken cancellationToken)
    {
        var schedule = await _service.CreateScheduleAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/work-dna/schedules/{schedule.Id}", schedule);
    }

    [HttpPut("schedules/{id:guid}")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<WorkActivityScheduleDto>> UpdateSchedule(Guid id, UpdateWorkActivityScheduleRequest request, CancellationToken cancellationToken)
    {
        var schedule = await _service.UpdateScheduleAsync(RequireActor(), id, request, cancellationToken);
        return schedule is null ? NotFound() : Ok(schedule);
    }

    [HttpPost("schedules/materialize")]
    [Authorize(Policy = "Operator")]
    public Task<MaterializeSchedulesResponse> Materialize([FromQuery] string? month, CancellationToken cancellationToken) =>
        _service.MaterializeSchedulesAsync(RequireActor(), month, null, cancellationToken);

    [HttpPost("schedules/{id:guid}/materialize")]
    [Authorize(Policy = "Operator")]
    public Task<MaterializeSchedulesResponse> MaterializeSchedule(Guid id, [FromQuery] string? month, CancellationToken cancellationToken) =>
        _service.MaterializeSchedulesAsync(RequireActor(), month, id, cancellationToken);

    [HttpGet("calendar-days")]
    public Task<IReadOnlyList<WorkCalendarDayEntryDto>> CalendarDays([FromQuery] string? month, CancellationToken cancellationToken) =>
        _service.ListCalendarDaysAsync(RequireTenantId(), month, cancellationToken);

    [HttpPost("calendar-days")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<ActionResult<WorkCalendarDayEntryDto>> SetCalendarDay(SetWorkCalendarDayRequest request, CancellationToken cancellationToken)
    {
        var day = await _service.SetCalendarDayAsync(RequireActor(), request, cancellationToken);
        return Ok(day);
    }

    [HttpDelete("calendar-days/{date}")]
    [Authorize(Policy = "TenantAdmin")]
    public async Task<IActionResult> RemoveCalendarDay(string date, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return BadRequest("Date must use the yyyy-MM-dd format.");
        }

        return await _service.RemoveCalendarDayAsync(RequireActor(), parsed, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpGet("leaves")]
    public Task<IReadOnlyList<WorkLeaveDto>> Leaves([FromQuery] string? month, CancellationToken cancellationToken) =>
        _service.ListLeavesAsync(RequireTenantId(), month, cancellationToken);

    [HttpPost("leaves")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<WorkLeaveDto>> CreateLeave(CreateWorkLeaveRequest request, CancellationToken cancellationToken)
    {
        var leave = await _service.CreateLeaveAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/work-dna/leaves/{leave.Id}", leave);
    }
}
