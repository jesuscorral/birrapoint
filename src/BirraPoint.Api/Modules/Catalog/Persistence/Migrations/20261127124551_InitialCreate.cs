using System;
using System.Text.Json;
using BirraPoint.Api.Modules.Catalog.Persistence.Seeding;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Modules.Catalog.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        private static readonly JsonSerializerOptions DescriptionJsonOptions =
            new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "BjcpStyles",
                schema: "catalog",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CategoryNumber = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OGLow = table.Column<decimal>(type: "numeric(4,3)", nullable: true),
                    OGHigh = table.Column<decimal>(type: "numeric(4,3)", nullable: true),
                    FGLow = table.Column<decimal>(type: "numeric(4,3)", nullable: true),
                    FGHigh = table.Column<decimal>(type: "numeric(4,3)", nullable: true),
                    IBULow = table.Column<int>(type: "integer", nullable: true),
                    IBUHigh = table.Column<int>(type: "integer", nullable: true),
                    SRMLow = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    SRMHigh = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    ABVLow = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    ABVHigh = table.Column<decimal>(type: "numeric(4,1)", nullable: true),
                    DescriptionJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BjcpStyles", x => x.Code);
                });

            SeedCatalog(migrationBuilder);
        }

        /// <summary>
        /// Seeds the full BJCP 2021 catalog from the embedded bjcp-2021.json resource (FR-049, ADR-0005); the
        /// JSON file stays the single source of truth and this migration never hardcodes catalog data.
        /// </summary>
        /// <remarks>
        /// Runs once per database. A later edit of bjcp-2021.json does not update migrated databases: a content
        /// correction needs its own migration. <see cref="BjcpStyleCatalogLoader.ComputeContentHash"/> is pinned by
        /// a unit test so an in-place edit fails fast instead of drifting silently.
        /// </remarks>
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

            migrationBuilder.InsertData(schema: "catalog", table: "BjcpStyles", columns: columns, values: values);
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BjcpStyles",
                schema: "catalog");
        }
    }
}
