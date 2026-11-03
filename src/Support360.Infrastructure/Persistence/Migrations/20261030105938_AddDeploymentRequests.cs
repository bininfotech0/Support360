using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Support360.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeploymentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetAgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeploymentPackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PackageVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PackageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChangeRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Environment = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedBySubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RequestedByDisplay = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentRequests_Agents_TargetAgentId",
                        column: x => x.TargetAgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeploymentRequests_ChangeRequests_ChangeRequestId",
                        column: x => x.ChangeRequestId,
                        principalTable: "ChangeRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeploymentRequests_DeploymentPackages_DeploymentPackageId",
                        column: x => x.DeploymentPackageId,
                        principalTable: "DeploymentPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRequests_ChangeRequestId",
                table: "DeploymentRequests",
                column: "ChangeRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRequests_DeploymentPackageId",
                table: "DeploymentRequests",
                column: "DeploymentPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRequests_TargetAgentId",
                table: "DeploymentRequests",
                column: "TargetAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRequests_TenantId_ChangeRequestId",
                table: "DeploymentRequests",
                columns: new[] { "TenantId", "ChangeRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentRequests_TenantId_TargetAgentId_Status",
                table: "DeploymentRequests",
                columns: new[] { "TenantId", "TargetAgentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentRequests");
        }
    }
}
