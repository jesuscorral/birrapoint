using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class AddJudgeRosterImport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BjcpId",
            table: "Judges",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BjcpRank",
            table: "Judges",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Preferences",
            table: "Judges",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PreferredCategory",
            table: "Judges",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "JudgeImportBatches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JudgeImportBatches", x => x.Id);
                table.ForeignKey(
                    name: "FK_JudgeImportBatches_Competitions_CompetitionId",
                    column: x => x.CompetitionId,
                    principalTable: "Competitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "JudgeImportRows",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                JudgeImportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                RowNumber = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                BjcpRank = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                BjcpId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                PreferredCategory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Preferences = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JudgeImportRows", x => x.Id);
                table.ForeignKey(
                    name: "FK_JudgeImportRows_JudgeImportBatches_JudgeImportBatchId",
                    column: x => x.JudgeImportBatchId,
                    principalTable: "JudgeImportBatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_JudgeImportBatches_CompetitionId",
            table: "JudgeImportBatches",
            column: "CompetitionId",
            unique: true,
            filter: "\"Status\" = 'Pending'");

        migrationBuilder.CreateIndex(
            name: "IX_JudgeImportRows_JudgeImportBatchId_RowNumber",
            table: "JudgeImportRows",
            columns: new[] { "JudgeImportBatchId", "RowNumber" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "JudgeImportRows");

        migrationBuilder.DropTable(
            name: "JudgeImportBatches");

        migrationBuilder.DropColumn(
            name: "BjcpId",
            table: "Judges");

        migrationBuilder.DropColumn(
            name: "BjcpRank",
            table: "Judges");

        migrationBuilder.DropColumn(
            name: "Preferences",
            table: "Judges");

        migrationBuilder.DropColumn(
            name: "PreferredCategory",
            table: "Judges");
    }
}
