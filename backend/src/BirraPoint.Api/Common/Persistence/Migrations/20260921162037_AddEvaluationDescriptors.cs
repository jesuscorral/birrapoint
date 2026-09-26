using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class AddEvaluationDescriptors : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DescriptorsJson",
            table: "Evaluations",
            type: "jsonb",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FeedbackComment",
            table: "Evaluations",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DescriptorsJson",
            table: "Evaluations");

        migrationBuilder.DropColumn(
            name: "FeedbackComment",
            table: "Evaluations");
    }
}
