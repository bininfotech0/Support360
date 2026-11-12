using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Support360.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkDna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkCalendarDays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DayType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCalendarDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkDnaCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActivityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkDnaCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkLeaves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Hours = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkLeaves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkLeaves_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkActivitySchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DnaCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    TargetMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EffortMinutes = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IgnoreOff = table.Column<bool>(type: "boolean", nullable: false),
                    BusinessImpact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Weekdays = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DayOfMonth = table.Column<int>(type: "integer", nullable: true),
                    StartHourUtc = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkActivitySchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkActivitySchedules_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkActivitySchedules_Users_AssigneeId",
                        column: x => x.AssigneeId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkActivitySchedules_WorkDnaCategories_DnaCategoryId",
                        column: x => x.DnaCategoryId,
                        principalTable: "WorkDnaCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssigneeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DnaCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduledFor = table.Column<DateOnly>(type: "date", nullable: true),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    TargetMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EffortMinutes = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IgnoreOff = table.Column<bool>(type: "boolean", nullable: false),
                    BusinessImpact = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkActivities_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkActivities_Users_AssigneeId",
                        column: x => x.AssigneeId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkActivities_WorkActivitySchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "WorkActivitySchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkActivities_WorkDnaCategories_DnaCategoryId",
                        column: x => x.DnaCategoryId,
                        principalTable: "WorkDnaCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TenantId_Name",
                table: "Projects",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivities_AssigneeId",
                table: "WorkActivities",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivities_DnaCategoryId",
                table: "WorkActivities",
                column: "DnaCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivities_ProjectId",
                table: "WorkActivities",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivities_ScheduleId",
                table: "WorkActivities",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivities_TenantId_AssigneeId_StartTime",
                table: "WorkActivities",
                columns: new[] { "TenantId", "AssigneeId", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivities_TenantId_ScheduleId_ScheduledFor",
                table: "WorkActivities",
                columns: new[] { "TenantId", "ScheduleId", "ScheduledFor" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivities_TenantId_StartTime",
                table: "WorkActivities",
                columns: new[] { "TenantId", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivitySchedules_AssigneeId",
                table: "WorkActivitySchedules",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivitySchedules_DnaCategoryId",
                table: "WorkActivitySchedules",
                column: "DnaCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivitySchedules_ProjectId",
                table: "WorkActivitySchedules",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkActivitySchedules_TenantId_IsActive",
                table: "WorkActivitySchedules",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCalendarDays_TenantId_Date",
                table: "WorkCalendarDays",
                columns: new[] { "TenantId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkDnaCategories_TenantId_CategoryName_ActivityName",
                table: "WorkDnaCategories",
                columns: new[] { "TenantId", "CategoryName", "ActivityName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkLeaves_TenantId_UserId_Date",
                table: "WorkLeaves",
                columns: new[] { "TenantId", "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkLeaves_UserId",
                table: "WorkLeaves",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkActivities");

            migrationBuilder.DropTable(
                name: "WorkCalendarDays");

            migrationBuilder.DropTable(
                name: "WorkLeaves");

            migrationBuilder.DropTable(
                name: "WorkActivitySchedules");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "WorkDnaCategories");
        }
    }
}
