using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class AddImportBatchAndImportRow : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ImportBatches",
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
                table.PrimaryKey("PK_ImportBatches", x => x.Id);
                table.ForeignKey(
                    name: "FK_ImportBatches_Competitions_CompetitionId",
                    column: x => x.CompetitionId,
                    principalTable: "Competitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ImportRows",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ImportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                RowNumber = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ParticipantName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ParticipantEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                BeerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                StyleText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CollaboratorsJson = table.Column<string>(type: "jsonb", nullable: false),
                ResolvedStyleCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImportRows", x => x.Id);
                table.ForeignKey(
                    name: "FK_ImportRows_ImportBatches_ImportBatchId",
                    column: x => x.ImportBatchId,
                    principalTable: "ImportBatches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ImportBatches_CompetitionId",
            table: "ImportBatches",
            column: "CompetitionId",
            unique: true,
            filter: "\"Status\" = 'Pending'");

        migrationBuilder.CreateIndex(
            name: "IX_ImportRows_ImportBatchId_RowNumber",
            table: "ImportRows",
            columns: new[] { "ImportBatchId", "RowNumber" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ImportRows");

        migrationBuilder.DropTable(
            name: "ImportBatches");
    }
}
