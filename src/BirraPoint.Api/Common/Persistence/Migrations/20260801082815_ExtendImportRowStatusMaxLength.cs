using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class ExtendImportRowStatusMaxLength : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "ImportRows",
            type: "character varying(25)",
            maxLength: 25,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "ImportRows",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(25)",
            oldMaxLength: 25);
    }
}
