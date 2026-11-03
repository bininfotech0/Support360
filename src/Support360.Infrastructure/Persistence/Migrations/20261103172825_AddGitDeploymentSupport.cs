using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Support360.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitDeploymentSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "DeploymentRequests",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "GitBranch",
                table: "DeploymentRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitRepoURL",
                table: "DeploymentRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetPath",
                table: "DeploymentRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitBranch",
                table: "DeploymentRequests");

            migrationBuilder.DropColumn(
                name: "GitRepoURL",
                table: "DeploymentRequests");

            migrationBuilder.DropColumn(
                name: "TargetPath",
                table: "DeploymentRequests");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "DeploymentRequests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
