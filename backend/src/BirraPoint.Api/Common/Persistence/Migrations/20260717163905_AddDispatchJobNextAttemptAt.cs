using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDispatchJobNextAttemptAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NextAttemptAt",
            table: "DispatchJobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_DispatchJobs_Status_NextAttemptAt",
            table: "DispatchJobs",
            columns: new[] { "Status", "NextAttemptAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_DispatchJobs_Status_NextAttemptAt",
            table: "DispatchJobs");

        migrationBuilder.DropColumn(
            name: "NextAttemptAt",
            table: "DispatchJobs");
    }
}
