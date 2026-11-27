using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Modules.Publication.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "publication");

            migrationBuilder.CreateTable(
                name: "GeneratedScoreSheets",
                schema: "publication",
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
                });

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "publication",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Handler = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_InboxMessages", x => new { x.EventId, x.Handler });
                });

            migrationBuilder.CreateTable(
                name: "ResultsArchives",
                schema: "publication",
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
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedScoreSheets_BeerEntryId",
                schema: "publication",
                table: "GeneratedScoreSheets",
                column: "BeerEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_publication_InboxMessages_ProcessedAt",
                schema: "publication",
                table: "InboxMessages",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ResultsArchives_CompetitionId",
                schema: "publication",
                table: "ResultsArchives",
                column: "CompetitionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneratedScoreSheets",
                schema: "publication");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "publication");

            migrationBuilder.DropTable(
                name: "ResultsArchives",
                schema: "publication");
        }
    }
}
