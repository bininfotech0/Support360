using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Support360.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Target = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastDurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    LastStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastErrorCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationChecks_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationCheckResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationCheckId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ErrorCategory = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorDetail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationCheckResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationCheckResults_ApplicationChecks_ApplicationCheckId",
                        column: x => x.ApplicationCheckId,
                        principalTable: "ApplicationChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationCheckResults_AgentId_EventId",
                table: "ApplicationCheckResults",
                columns: new[] { "AgentId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationCheckResults_ApplicationCheckId",
                table: "ApplicationCheckResults",
                column: "ApplicationCheckId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationCheckResults_TenantId_ApplicationCheckId_Started~",
                table: "ApplicationCheckResults",
                columns: new[] { "TenantId", "ApplicationCheckId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationChecks_AgentId",
                table: "ApplicationChecks",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationChecks_TenantId_AgentId_Enabled",
                table: "ApplicationChecks",
                columns: new[] { "TenantId", "AgentId", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationChecks_TenantId_AgentId_Name",
                table: "ApplicationChecks",
                columns: new[] { "TenantId", "AgentId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationCheckResults");

            migrationBuilder.DropTable(
                name: "ApplicationChecks");
        }
    }
}
