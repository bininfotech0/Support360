using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Support360.Application.Abstractions;
using Support360.Contracts.Auth;
using Support360.Contracts.WorkDna;
using Support360.Domain;
using Support360.Domain.Entities;
using Support360.Infrastructure.Persistence;

namespace Support360.Tests.Integration;

public sealed class WorkDnaTests : IClassFixture<Support360ApiFactory>
{
    private const string Password = "Work-Dna-Password-123!";

    private readonly Support360ApiFactory _factory;

    public WorkDnaTests(Support360ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task WorkDnaLifecycleTracksCapacityCalendarSchedulesAndRolePolicies()
    {
        using var admin = await CreateAuthorizedClientAsync(Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);

        var lookups = await admin.GetFromJsonAsync<WorkDnaLookupsDto>("/api/work-dna/lookups");
        Assert.NotNull(lookups);
        Assert.False(string.IsNullOrWhiteSpace(lookups!.OrganizationName));
        Assert.NotEmpty(lookups.Projects);
        Assert.NotEmpty(lookups.Categories);
        var adminMember = Assert.Single(lookups.Team, member => member.Email == Support360ApiFactory.AdminEmail);

        var project = lookups.Projects[0];
        var category = lookups.Categories[0];

        var createdProject = await admin.PostAsJsonAsync("/api/projects", new CreateProjectRequest($"Payments {Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Created, createdProject.StatusCode);
        var duplicateProject = await admin.PostAsJsonAsync("/api/projects", new CreateProjectRequest(project.Name));
        Assert.Equal(HttpStatusCode.Conflict, duplicateProject.StatusCode);

        var createdCategory = await admin.PostAsJsonAsync(
            "/api/work-dna/categories",
            new CreateWorkDnaCategoryRequest("Monitoring", $"Daily health check {Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Created, createdCategory.StatusCode);
        var newCategory = (await createdCategory.Content.ReadFromJsonAsync<WorkDnaCategoryDto>())!;

        var nextMonth = DateTime.UtcNow.AddMonths(1);
        var month = $"{nextMonth.Year:D4}-{nextMonth.Month:D2}";
        var start = new DateTimeOffset(nextMonth.Year, nextMonth.Month, 15, 9, 0, 0, TimeSpan.Zero);
        var activityRequest = new CreateWorkActivityRequest(
            project.Id,
            adminMember.Id,
            newCategory.Id,
            "Review payment exceptions",
            "Check failed payment attempts and retry safe cases.",
            "Auto",
            90,
            "High",
            start,
            start.AddMinutes(90),
            false,
            "Payment reconciliation SLA");

        var invalidActivity = await admin.PostAsJsonAsync("/api/work-dna/activities", activityRequest with { EffortMinutes = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidActivity.StatusCode);

        var createdActivity = await admin.PostAsJsonAsync("/api/work-dna/activities", activityRequest);
        Assert.Equal(HttpStatusCode.Created, createdActivity.StatusCode);
        var activity = (await createdActivity.Content.ReadFromJsonAsync<WorkActivityDto>())!;
        Assert.Equal("Scheduled", activity.Status);
        Assert.Equal("1h 30m", activity.Effort);
        Assert.Equal(newCategory.CategoryName, activity.CategoryName);

        var activities = await admin.GetFromJsonAsync<List<WorkActivityDto>>($"/api/work-dna/activities?month={month}");
        Assert.NotNull(activities);
        Assert.Contains(activities!, item => item.Id == activity.Id);

        var capacity = await admin.GetFromJsonAsync<WorkCapacityDto>($"/api/work-dna/capacity?month={month}");
        Assert.NotNull(capacity);
        Assert.True(capacity!.TotalWorkingDays > 0);
        Assert.Equal(capacity.TotalWorkingDays * 8.0, capacity.WorkingCapacityHours);
        Assert.Equal(1.5, capacity.BookedHours);
        Assert.True(capacity.PendingActivities >= 1);
        Assert.Equal(month, capacity.Month);

        var completed = await admin.PostAsync($"/api/work-dna/activities/{activity.Id}/complete", null);
        completed.EnsureSuccessStatusCode();
        var completedActivity = (await completed.Content.ReadFromJsonAsync<WorkActivityDto>())!;
        Assert.Equal("Completed", completedActivity.Status);
        Assert.NotNull(completedActivity.CompletedAt);

        var holiday = start.AddDays(1).ToString("yyyy-MM-dd");
        var holidayResponse = await admin.PostAsJsonAsync(
            "/api/work-dna/calendar-days",
            new SetWorkCalendarDayRequest(DateOnly.Parse(holiday), "Holiday", "Test holiday"));
        holidayResponse.EnsureSuccessStatusCode();
        var calendarDays = await admin.GetFromJsonAsync<List<WorkCalendarDayEntryDto>>($"/api/work-dna/calendar-days?month={month}");
        Assert.Contains(calendarDays!, day => day.Date.ToString("yyyy-MM-dd") == holiday && day.DayType == "Holiday");

        var leaveDate = DateOnly.FromDateTime(start.AddDays(2).Date);
        var leaveResponse = await admin.PostAsJsonAsync(
            "/api/work-dna/leaves",
            new CreateWorkLeaveRequest(adminMember.Id, leaveDate, 8, "Appointment"));
        leaveResponse.EnsureSuccessStatusCode();
        var leaves = await admin.GetFromJsonAsync<List<WorkLeaveDto>>($"/api/work-dna/leaves?month={month}");
        Assert.Contains(leaves!, leave => leave.UserId == adminMember.Id && leave.Date == leaveDate);

        var adjustedCapacity = await admin.GetFromJsonAsync<WorkCapacityDto>($"/api/work-dna/capacity?month={month}");
        Assert.NotNull(adjustedCapacity);
        Assert.True(adjustedCapacity!.HolidayDays >= 1);
        Assert.True(adjustedCapacity.LeaveDays >= 1);
        Assert.True(adjustedCapacity.WorkingCapacityHours < capacity.WorkingCapacityHours);

        var holidayStart = new DateTimeOffset(nextMonth.Year, nextMonth.Month, 16, 9, 0, 0, TimeSpan.Zero);
        var suppressedRequest = activityRequest with
        {
            Title = "Holiday triage",
            StartTime = holidayStart,
            EndTime = holidayStart.AddMinutes(60),
            EffortMinutes = 60,
            IgnoreOff = false,
        };
        var suppressedResponse = await admin.PostAsJsonAsync("/api/work-dna/activities", suppressedRequest);
        Assert.Equal(HttpStatusCode.Created, suppressedResponse.StatusCode);
        var suppressedActivity = (await suppressedResponse.Content.ReadFromJsonAsync<WorkActivityDto>())!;
        Assert.True(suppressedActivity.IsSuppressed);

        var overrideRequest = activityRequest with
        {
            Title = "Holiday release",
            StartTime = holidayStart,
            EndTime = holidayStart.AddMinutes(30),
            EffortMinutes = 30,
            IgnoreOff = true,
        };
        var overrideResponse = await admin.PostAsJsonAsync("/api/work-dna/activities", overrideRequest);
        Assert.Equal(HttpStatusCode.Created, overrideResponse.StatusCode);
        var overrideActivity = (await overrideResponse.Content.ReadFromJsonAsync<WorkActivityDto>())!;
        Assert.False(overrideActivity.IsSuppressed);

        var suppressionCapacity = await admin.GetFromJsonAsync<WorkCapacityDto>($"/api/work-dna/capacity?month={month}");
        Assert.NotNull(suppressionCapacity);
        Assert.Equal(2.0, suppressionCapacity!.BookedHours);
        var holidayDay = Assert.Single(suppressionCapacity.Days, day => day.Date.ToString("yyyy-MM-dd") == holiday);
        Assert.Equal(1, holidayDay.Suppressed);

        var range = await admin.GetFromJsonAsync<WorkCapacityRangeDto>($"/api/work-dna/capacity/range?from={month}&to={month}");
        Assert.NotNull(range);
        var rangeMonth = Assert.Single(range!.Months);
        Assert.Equal(month, rangeMonth.Month);
        Assert.Equal(month, range.From);
        Assert.Equal(month, range.To);

        var tooLongRange = await admin.GetAsync(
            $"/api/work-dna/capacity/range?from={nextMonth.AddMonths(-12):yyyy-MM}&to={month}");
        Assert.Equal(HttpStatusCode.BadRequest, tooLongRange.StatusCode);

        var sourceReference = Guid.NewGuid().ToString("N");
        var autoCaptureRequest = new AutoCaptureWorkActivityRequest(
            project.Id,
            adminMember.Id,
            newCategory.Id,
            "Diagnostic auto-capture",
            "Captured from a diagnostic signal.",
            30,
            "Medium",
            "Diagnostic coverage",
            "DiagnosticEngine",
            sourceReference,
            start);
        var autoCaptured = await admin.PostAsJsonAsync("/api/work-dna/activities/auto-capture", autoCaptureRequest);
        Assert.Equal(HttpStatusCode.Created, autoCaptured.StatusCode);
        var autoActivity = (await autoCaptured.Content.ReadFromJsonAsync<WorkActivityDto>())!;
        Assert.Equal("Auto", autoActivity.TargetMode);
        Assert.True(autoActivity.IgnoreOff);

        var duplicateCapture = await admin.PostAsJsonAsync("/api/work-dna/activities/auto-capture", autoCaptureRequest);
        Assert.Equal(HttpStatusCode.Created, duplicateCapture.StatusCode);
        var duplicateActivity = (await duplicateCapture.Content.ReadFromJsonAsync<WorkActivityDto>())!;
        Assert.Equal(autoActivity.Id, duplicateActivity.Id);

        var scheduleRequest = new CreateWorkActivityScheduleRequest(
            project.Id,
            adminMember.Id,
            newCategory.Id,
            "Daily payment sweep",
            "Recurring payment sweep.",
            "Manual",
            60,
            "Medium",
            false,
            "Daily payment visibility",
            "Daily",
            [],
            null,
            9,
            new DateOnly(nextMonth.Year, nextMonth.Month, 1),
            new DateOnly(nextMonth.Year, nextMonth.Month, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)));

        var createdSchedule = await admin.PostAsJsonAsync("/api/work-dna/schedules", scheduleRequest);
        Assert.Equal(HttpStatusCode.Created, createdSchedule.StatusCode);
        var schedule = (await createdSchedule.Content.ReadFromJsonAsync<WorkActivityScheduleDto>())!;
        Assert.Equal("Daily", schedule.Frequency);
        Assert.True(schedule.IsActive);

        var materialized = await admin.PostAsync($"/api/work-dna/schedules/{schedule.Id}/materialize?month={month}", null);
        materialized.EnsureSuccessStatusCode();
        var materializedResult = (await materialized.Content.ReadFromJsonAsync<MaterializeSchedulesResponse>())!;
        Assert.True(materializedResult.Generated > 0);
        Assert.Equal(month, materializedResult.Month);

        var afterMaterialize = await admin.GetFromJsonAsync<List<WorkActivityDto>>($"/api/work-dna/activities?month={month}");
        Assert.Contains(afterMaterialize!, item => item.ScheduleId == schedule.Id && item.Title == "Daily payment sweep");

        var secondMaterialize = await admin.PostAsync($"/api/work-dna/schedules/{schedule.Id}/materialize?month={month}", null);
        secondMaterialize.EnsureSuccessStatusCode();
        var secondResult = (await secondMaterialize.Content.ReadFromJsonAsync<MaterializeSchedulesResponse>())!;
        Assert.Equal(0, secondResult.Generated);
        Assert.True(secondResult.Skipped > 0);

        var viewerEmail = await SeedViewerAsync();
        using var viewer = await CreateAuthorizedClientAsync(viewerEmail, Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/work-dna/activities", activityRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/work-dna/schedules", scheduleRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/work-dna/capacity?month={month}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/work-dna/lookups")).StatusCode);
    }

    private async Task<string> SeedViewerAsync()
    {
        var email = $"work-dna-viewer-{Guid.NewGuid():N}@support360.local";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        tenantContext.SetSystem();
        var tenantId = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Slug == "integration")
            .Select(t => t.Id)
            .SingleAsync();
        db.Users.Add(new User
        {
            TenantId = tenantId,
            Email = email,
            EmailNormalized = email,
            DisplayName = "Work DNA Viewer",
            PasswordHash = hasher.Hash(Password),
            Role = UserRole.Viewer
        });
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();
        var response = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", response.AccessToken);
        return client;
    }
}
