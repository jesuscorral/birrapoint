using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDispatchJobLease : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LeaseExpiresAt",
            table: "DispatchJobs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LeaseOwner",
            table: "DispatchJobs",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_DispatchJobs_Status_LeaseExpiresAt",
            table: "DispatchJobs",
            columns: new[] { "Status", "LeaseExpiresAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_DispatchJobs_Status_LeaseExpiresAt",
            table: "DispatchJobs");

        migrationBuilder.DropColumn(
            name: "LeaseExpiresAt",
            table: "DispatchJobs");

        migrationBuilder.DropColumn(
            name: "LeaseOwner",
            table: "DispatchJobs");
    }
}
