using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCompetitionCategories : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CompetitionCategories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompetitionCategories", x => x.Id);
                table.ForeignKey(
                    name: "FK_CompetitionCategories_Competitions_CompetitionId",
                    column: x => x.CompetitionId,
                    principalTable: "Competitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "CompetitionCategoryStyles",
            columns: table => new
            {
                CompetitionCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                StyleCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CompetitionCategoryStyles", x => new { x.CompetitionCategoryId, x.StyleCode });
                table.ForeignKey(
                    name: "FK_CompetitionCategoryStyles_BjcpStyles_StyleCode",
                    column: x => x.StyleCode,
                    principalTable: "BjcpStyles",
                    principalColumn: "Code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CompetitionCategoryStyles_CompetitionCategories_Competition~",
                    column: x => x.CompetitionCategoryId,
                    principalTable: "CompetitionCategories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CompetitionCategoryStyles_Competitions_CompetitionId",
                    column: x => x.CompetitionId,
                    principalTable: "Competitions",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_CompetitionCategories_CompetitionId_Name",
            table: "CompetitionCategories",
            columns: new[] { "CompetitionId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CompetitionCategoryStyles_CompetitionId_StyleCode",
            table: "CompetitionCategoryStyles",
            columns: new[] { "CompetitionId", "StyleCode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CompetitionCategoryStyles_StyleCode",
            table: "CompetitionCategoryStyles",
            column: "StyleCode");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CompetitionCategoryStyles");

        migrationBuilder.DropTable(
            name: "CompetitionCategories");
    }
}
