using System.Text.Json;
using BirraPoint.Api.Features.Catalog.Data;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBjcpStyleCatalogDetails : Migration
{
    private static readonly JsonSerializerOptions DescriptionJsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Code",
            table: "BjcpStyles",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(5)",
            oldMaxLength: 5);

        migrationBuilder.AddColumn<decimal>(
            name: "ABVHigh",
            table: "BjcpStyles",
            type: "numeric(4,1)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "ABVLow",
            table: "BjcpStyles",
            type: "numeric(4,1)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DescriptionJson",
            table: "BjcpStyles",
            type: "jsonb",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<decimal>(
            name: "FGHigh",
            table: "BjcpStyles",
            type: "numeric(4,3)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "FGLow",
            table: "BjcpStyles",
            type: "numeric(4,3)",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "IBUHigh",
            table: "BjcpStyles",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "IBULow",
            table: "BjcpStyles",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "OGHigh",
            table: "BjcpStyles",
            type: "numeric(4,3)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "OGLow",
            table: "BjcpStyles",
            type: "numeric(4,3)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SRMHigh",
            table: "BjcpStyles",
            type: "numeric(5,1)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "SRMLow",
            table: "BjcpStyles",
            type: "numeric(5,1)",
            nullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "StyleCode",
            table: "BeerEntries",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(5)",
            oldMaxLength: 5);

        SeedCatalog(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Must run before narrowing Code/StyleCode back to varchar(5): several seeded
        // slug-style codes (e.g. "27-KentuckyCommon") no longer fit that width.
        migrationBuilder.Sql("DELETE FROM \"BjcpStyles\";");

        migrationBuilder.DropColumn(
            name: "ABVHigh",
            table: "BjcpStyles");

        migrationBuilder.DropColumn(
            name: "ABVLow",
            table: "BjcpStyles");

        migrationBuilder.DropColumn(
            name: "DescriptionJson",
            table: "BjcpStyles");

        migrationBuilder.DropColumn(
            name: "FGHigh",
            table: "BjcpStyles");

        migrationBuilder.DropColumn(
            name: "FGLow",
            table: "BjcpStyles");

        migrationBuilder.DropColumn(
            name: "IBUHigh",
            table: "BjcpStyles");

        migrationBuilder.DropColumn(
            name: "IBULow",
            table: "BjcpStyles");

        migrationBuilder.DropColumn(
            name: "OGHigh",
            table: "BjcpStyles");

        migrationBuilder.DropColumn(
            name: "OGLow",
            table: "BjcpStyles");

        migrationBuilder.DropColumn(
            name: "SRMHigh",
            table: "BjcpStyles");

        migrationBuilder.DropColumn(
            name: "SRMLow",
            table: "BjcpStyles");

        migrationBuilder.AlterColumn<string>(
            name: "Code",
            table: "BjcpStyles",
            type: "character varying(5)",
            maxLength: 5,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20);

        migrationBuilder.AlterColumn<string>(
            name: "StyleCode",
            table: "BeerEntries",
            type: "character varying(5)",
            maxLength: 5,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20);
    }

    /// <summary>
    /// Seeds the full BJCP 2021 catalog (125 entries, categories 1–34 + Appendix B) from the
    /// embedded bjcp-2021.json resource — the JSON file stays the single source of truth
    /// (T010, R-12); this migration never hardcodes catalog data.
    /// </summary>
    private static void SeedCatalog(MigrationBuilder migrationBuilder)
    {
        var styles = BjcpStyleCatalogLoader.Load();
        var seededAt = DateTimeOffset.UtcNow;

        var columns = new[]
        {
            "Code", "Name", "CategoryNumber", "CategoryName",
            "OGLow", "OGHigh", "FGLow", "FGHigh", "IBULow", "IBUHigh", "SRMLow", "SRMHigh", "ABVLow", "ABVHigh",
            "DescriptionJson", "CreatedAt", "UpdatedAt",
        };

        var values = new object[styles.Count, columns.Length];
        for (var i = 0; i < styles.Count; i++)
        {
            var style = styles[i];
            var vitals = style.VitalStatistics;

            values[i, 0] = style.Code;
            values[i, 1] = style.Name;
            values[i, 2] = style.CategoryNumber;
            values[i, 3] = style.CategoryName;
            values[i, 4] = (object)vitals.OgLow;
            values[i, 5] = (object)vitals.OgHigh;
            values[i, 6] = (object)vitals.FgLow;
            values[i, 7] = (object)vitals.FgHigh;
            values[i, 8] = (object)vitals.IbuLow;
            values[i, 9] = (object)vitals.IbuHigh;
            values[i, 10] = (object)vitals.SrmLow;
            values[i, 11] = (object)vitals.SrmHigh;
            values[i, 12] = (object)vitals.AbvLow;
            values[i, 13] = (object)vitals.AbvHigh;
            values[i, 14] = JsonSerializer.Serialize(style.Description, DescriptionJsonOptions);
            values[i, 15] = seededAt;
            values[i, 16] = seededAt;
        }

        migrationBuilder.InsertData(table: "BjcpStyles", columns: columns, values: values);
    }
}
