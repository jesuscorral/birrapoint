using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class AddResultsDispatchStorage : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "GeneratedScoreSheets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BeerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                PdfBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GeneratedScoreSheets", x => x.Id);
                table.ForeignKey(
                    name: "FK_GeneratedScoreSheets_BeerEntries_BeerEntryId",
                    column: x => x.BeerEntryId,
                    principalTable: "BeerEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ResultsArchives",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                ZipBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ResultsArchives", x => x.Id);
                table.ForeignKey(
                    name: "FK_ResultsArchives_Competitions_CompetitionId",
                    column: x => x.CompetitionId,
                    principalTable: "Competitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_GeneratedScoreSheets_BeerEntryId",
            table: "GeneratedScoreSheets",
            column: "BeerEntryId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ResultsArchives_CompetitionId",
            table: "ResultsArchives",
            column: "CompetitionId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "GeneratedScoreSheets");

        migrationBuilder.DropTable(
            name: "ResultsArchives");
    }
}
