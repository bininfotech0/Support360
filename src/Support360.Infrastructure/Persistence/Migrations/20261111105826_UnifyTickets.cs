using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Support360.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnifyTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeploymentRequests_ChangeRequests_ChangeRequestId",
                table: "DeploymentRequests");

            migrationBuilder.RenameColumn(
                name: "ChangeRequestId",
                table: "DeploymentRequests",
                newName: "ChangeTicketId");

            migrationBuilder.RenameIndex(
                name: "IX_DeploymentRequests_TenantId_ChangeRequestId",
                table: "DeploymentRequests",
                newName: "IX_DeploymentRequests_TenantId_ChangeTicketId");

            migrationBuilder.RenameIndex(
                name: "IX_DeploymentRequests_ChangeRequestId",
                table: "DeploymentRequests",
                newName: "IX_DeploymentRequests_ChangeTicketId");

            migrationBuilder.CreateTable(
                name: "TicketNumberSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastIssued = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketNumberSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Impact = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Urgency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedBySubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RequestedByDisplay = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RelatedBatchJobExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Risk = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TargetEnvironment = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PlannedStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlannedEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequiredApprovals = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tickets_BatchJobExecutions_RelatedBatchJobExecutionId",
                        column: x => x.RelatedBatchJobExecutionId,
                        principalTable: "BatchJobExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TicketActivities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorDisplay = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketActivities_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LinkedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketAlerts_Alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "Alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketAlerts_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApproverSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ApproverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApproverDisplay = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketApprovals_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TicketLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetTicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketLinks_Tickets_SourceTicketId",
                        column: x => x.SourceTicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TicketLinks_Tickets_TargetTicketId",
                        column: x => x.TargetTicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketActivities_TenantId_TicketId_OccurredAt",
                table: "TicketActivities",
                columns: new[] { "TenantId", "TicketId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketActivities_TicketId",
                table: "TicketActivities",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketAlerts_AlertId",
                table: "TicketAlerts",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketAlerts_TenantId_TicketId_AlertId",
                table: "TicketAlerts",
                columns: new[] { "TenantId", "TicketId", "AlertId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketAlerts_TicketId",
                table: "TicketAlerts",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketApprovals_TenantId_TicketId_ApproverSubject",
                table: "TicketApprovals",
                columns: new[] { "TenantId", "TicketId", "ApproverSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketApprovals_TicketId",
                table: "TicketApprovals",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketLinks_SourceTicketId",
                table: "TicketLinks",
                column: "SourceTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketLinks_TargetTicketId",
                table: "TicketLinks",
                column: "TargetTicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketLinks_TenantId_SourceTicketId_TargetTicketId_LinkType",
                table: "TicketLinks",
                columns: new[] { "TenantId", "SourceTicketId", "TargetTicketId", "LinkType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketLinks_TenantId_TargetTicketId",
                table: "TicketLinks",
                columns: new[] { "TenantId", "TargetTicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketNumberSequences_TenantId_Type",
                table: "TicketNumberSequences",
                columns: new[] { "TenantId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_RelatedBatchJobExecutionId",
                table: "Tickets",
                column: "RelatedBatchJobExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TenantId_AssignedToUserId_Status",
                table: "Tickets",
                columns: new[] { "TenantId", "AssignedToUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TenantId_Number",
                table: "Tickets",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TenantId_Status_Priority_OpenedAt",
                table: "Tickets",
                columns: new[] { "TenantId", "Status", "Priority", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TenantId_Type_Status_OpenedAt",
                table: "Tickets",
                columns: new[] { "TenantId", "Type", "Status", "OpenedAt" });

            // ---- Data migration: legacy registers -> Tickets (IDs preserved) ----
            migrationBuilder.Sql("""
                INSERT INTO "Tickets" ("Id", "TenantId", "Type", "Number", "Title", "Description", "Category", "Status", "Priority", "Impact", "Urgency", "RequestedByUserId", "RequestedBySubject", "RequestedByDisplay", "AssignedToUserId", "RelatedBatchJobExecutionId", "Risk", "TargetEnvironment", "PlannedStartAt", "PlannedEndAt", "RequiredApprovals", "ConcurrencyToken", "OpenedAt", "AcknowledgedAt", "ResolvedAt", "ClosedAt", "CreatedAt", "UpdatedAt")
                SELECT "Id", "TenantId", 'Incident', "Number", "Title", "Description", NULL, "Status", "Severity",
                       CASE "Severity" WHEN 'Critical' THEN 'High' WHEN 'High' THEN 'High' WHEN 'Medium' THEN 'Medium' ELSE 'Low' END,
                       CASE "Severity" WHEN 'Critical' THEN 'High' WHEN 'High' THEN 'High' WHEN 'Medium' THEN 'Medium' ELSE 'Low' END,
                       "CreatedByUserId", NULL, NULL, "AssignedToUserId", "RelatedBatchJobExecutionId", NULL, NULL, NULL, NULL, 1,
                       gen_random_uuid(), "OpenedAt", "AcknowledgedAt", "ResolvedAt", "ClosedAt", "CreatedAt", "UpdatedAt"
                FROM "Incidents";

                INSERT INTO "Tickets" ("Id", "TenantId", "Type", "Number", "Title", "Description", "Category", "Status", "Priority", "Impact", "Urgency", "RequestedByUserId", "RequestedBySubject", "RequestedByDisplay", "AssignedToUserId", "RelatedBatchJobExecutionId", "Risk", "TargetEnvironment", "PlannedStartAt", "PlannedEndAt", "RequiredApprovals", "ConcurrencyToken", "OpenedAt", "AcknowledgedAt", "ResolvedAt", "ClosedAt", "CreatedAt", "UpdatedAt")
                SELECT "Id", "TenantId", 'ServiceRequest', "Number", "Title", "Description", "Category", "Status", 'Medium', 'Medium', 'Medium',
                       "RequestedByUserId", "RequestedBySubject", "RequestedByDisplay", "AssignedToUserId", NULL, NULL, NULL, NULL, NULL, 1,
                       gen_random_uuid(), "OpenedAt", "AcknowledgedAt", "ResolvedAt", "ClosedAt", "CreatedAt", "UpdatedAt"
                FROM "ServiceRequests";

                INSERT INTO "Tickets" ("Id", "TenantId", "Type", "Number", "Title", "Description", "Category", "Status", "Priority", "Impact", "Urgency", "RequestedByUserId", "RequestedBySubject", "RequestedByDisplay", "AssignedToUserId", "RelatedBatchJobExecutionId", "Risk", "TargetEnvironment", "PlannedStartAt", "PlannedEndAt", "RequiredApprovals", "ConcurrencyToken", "OpenedAt", "AcknowledgedAt", "ResolvedAt", "ClosedAt", "CreatedAt", "UpdatedAt")
                SELECT "Id", "TenantId", 'Change', "Number", "Title", "Description", NULL, "Status", 'Medium', 'Medium', 'Medium',
                       "RequestedByUserId", "RequestedBySubject", "RequestedByDisplay", NULL, NULL, "Risk", "TargetEnvironment", "PlannedStartAt", "PlannedEndAt", "RequiredApprovals",
                       "ConcurrencyToken", "CreatedAt", NULL, NULL, NULL, "CreatedAt", "UpdatedAt"
                FROM "ChangeRequests";

                INSERT INTO "TicketAlerts" ("Id", "TenantId", "TicketId", "AlertId", "LinkedAt", "LinkedByUserId", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), "TenantId", "IncidentId", "AlertId", "LinkedAt", "LinkedByUserId", "CreatedAt", "UpdatedAt"
                FROM "IncidentAlerts";

                INSERT INTO "TicketApprovals" ("Id", "TenantId", "TicketId", "ApproverSubject", "ApproverUserId", "ApproverDisplay", "Decision", "Comment", "DecidedAt", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), "TenantId", "ChangeRequestId", "ApproverSubject", "ApproverUserId", "ApproverDisplay", "Decision", "Comment", "DecidedAt", "CreatedAt", "UpdatedAt"
                FROM "ChangeApprovals";

                INSERT INTO "TicketLinks" ("Id", "TenantId", "SourceTicketId", "TargetTicketId", "LinkType", "LinkedAt", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), "TenantId", "Id", "RelatedIncidentId", 'RelatesTo', "CreatedAt", "CreatedAt", "UpdatedAt"
                FROM "ChangeRequests"
                WHERE "RelatedIncidentId" IS NOT NULL;

                INSERT INTO "TicketActivities" ("Id", "TenantId", "TicketId", "Kind", "FromStatus", "ToStatus", "Detail", "ActorUserId", "ActorDisplay", "OccurredAt", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), "TenantId", "Id", 'created', NULL, "Status", 'Migrated from legacy Incidents register', "CreatedByUserId", NULL, "OpenedAt", "CreatedAt", "UpdatedAt"
                FROM "Incidents"
                UNION ALL
                SELECT gen_random_uuid(), "TenantId", "Id", 'created', NULL, "Status", 'Migrated from legacy ServiceRequests register', "RequestedByUserId", "RequestedByDisplay", "OpenedAt", "CreatedAt", "UpdatedAt"
                FROM "ServiceRequests"
                UNION ALL
                SELECT gen_random_uuid(), "TenantId", "Id", 'created', NULL, "Status", 'Migrated from legacy ChangeRequests register', "RequestedByUserId", "RequestedByDisplay", "CreatedAt", "CreatedAt", "UpdatedAt"
                FROM "ChangeRequests";
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_DeploymentRequests_Tickets_ChangeTicketId",
                table: "DeploymentRequests",
                column: "ChangeTicketId",
                principalTable: "Tickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropTable(
                name: "ChangeApprovals");

            migrationBuilder.DropTable(
                name: "IncidentAlerts");

            migrationBuilder.DropTable(
                name: "ServiceRequests");

            migrationBuilder.DropTable(
                name: "ChangeRequests");

            migrationBuilder.DropTable(
                name: "Incidents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeploymentRequests_Tickets_ChangeTicketId",
                table: "DeploymentRequests");

            migrationBuilder.DropTable(
                name: "TicketActivities");

            migrationBuilder.DropTable(
                name: "TicketAlerts");

            migrationBuilder.DropTable(
                name: "TicketApprovals");

            migrationBuilder.DropTable(
                name: "TicketLinks");

            migrationBuilder.DropTable(
                name: "TicketNumberSequences");

            migrationBuilder.DropTable(
                name: "Tickets");

            migrationBuilder.RenameColumn(
                name: "ChangeTicketId",
                table: "DeploymentRequests",
                newName: "ChangeRequestId");

            migrationBuilder.RenameIndex(
                name: "IX_DeploymentRequests_TenantId_ChangeTicketId",
                table: "DeploymentRequests",
                newName: "IX_DeploymentRequests_TenantId_ChangeRequestId");

            migrationBuilder.RenameIndex(
                name: "IX_DeploymentRequests_ChangeTicketId",
                table: "DeploymentRequests",
                newName: "IX_DeploymentRequests_ChangeRequestId");

            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RelatedBatchJobExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Incidents_BatchJobExecutions_RelatedBatchJobExecutionId",
                        column: x => x.RelatedBatchJobExecutionId,
                        principalTable: "BatchJobExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ServiceRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestedByDisplay = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedBySubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RelatedIncidentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConcurrencyToken = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PlannedEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlannedStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestedByDisplay = table.Column<string>(type: "text", nullable: false),
                    RequestedBySubject = table.Column<string>(type: "text", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequiredApprovals = table.Column<int>(type: "integer", nullable: false),
                    Risk = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetEnvironment = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_Incidents_RelatedIncidentId",
                        column: x => x.RelatedIncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "IncidentAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LinkedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LinkedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentAlerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentAlerts_Alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "Alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IncidentAlerts_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChangeApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApproverDisplay = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApproverSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ApproverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeApprovals_ChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "ChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeApprovals_ChangeRequestId",
                table: "ChangeApprovals",
                column: "ChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeApprovals_TenantId_ChangeRequestId_ApproverSubject",
                table: "ChangeApprovals",
                columns: new[] { "TenantId", "ChangeRequestId", "ApproverSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_RelatedIncidentId",
                table: "ChangeRequests",
                column: "RelatedIncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_TenantId_Number",
                table: "ChangeRequests",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_TenantId_Status_PlannedStartAt",
                table: "ChangeRequests",
                columns: new[] { "TenantId", "Status", "PlannedStartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAlerts_AlertId",
                table: "IncidentAlerts",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAlerts_IncidentId",
                table: "IncidentAlerts",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAlerts_TenantId_IncidentId_AlertId",
                table: "IncidentAlerts",
                columns: new[] { "TenantId", "IncidentId", "AlertId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_RelatedBatchJobExecutionId",
                table: "Incidents",
                column: "RelatedBatchJobExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_TenantId_Number",
                table: "Incidents",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_TenantId_Status_Severity_OpenedAt",
                table: "Incidents",
                columns: new[] { "TenantId", "Status", "Severity", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_TenantId_Number",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_TenantId_Status_OpenedAt",
                table: "ServiceRequests",
                columns: new[] { "TenantId", "Status", "OpenedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_DeploymentRequests_ChangeRequests_ChangeRequestId",
                table: "DeploymentRequests",
                column: "ChangeRequestId",
                principalTable: "ChangeRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
