using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class RewriteImportForAcceFormat : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CollaboratorsJson",
            table: "ImportRows");

        migrationBuilder.AddColumn<string>(
            name: "AcceMemberNumber",
            table: "Participants",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "DateOfBirth",
            table: "Participants",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Phone",
            table: "Participants",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "AbvPercent",
            table: "ImportRows",
            type: "numeric(4,2)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AcceMemberNumberText",
            table: "ImportRows",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "BottlingDate",
            table: "ImportRows",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "BrewDate",
            table: "ImportRows",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CategoryText",
            table: "ImportRows",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "DateOfBirth",
            table: "ImportRows",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EntryInstructions",
            table: "ImportRows",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Hops",
            table: "ImportRows",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Malts",
            table: "ImportRows",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OtherIngredients",
            table: "ImportRows",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Phone",
            table: "ImportRows",
            type: "character varying(30)",
            maxLength: 30,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ResolvedCompetitionCategoryId",
            table: "ImportRows",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "SubmittedAt",
            table: "ImportRows",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Yeast",
            table: "ImportRows",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "BeerName",
            table: "BeerEntries",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200);

        migrationBuilder.AddColumn<decimal>(
            name: "AbvPercent",
            table: "BeerEntries",
            type: "numeric(4,2)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<DateOnly>(
            name: "BottlingDate",
            table: "BeerEntries",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<DateOnly>(
            name: "BrewDate",
            table: "BeerEntries",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "CompetitionCategoryId",
            table: "BeerEntries",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EntryInstructions",
            table: "BeerEntries",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Hops",
            table: "BeerEntries",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Malts",
            table: "BeerEntries",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OtherIngredients",
            table: "BeerEntries",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "SubmittedAt",
            table: "BeerEntries",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

        migrationBuilder.AddColumn<string>(
            name: "Yeast",
            table: "BeerEntries",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_BeerEntries_CompetitionCategoryId",
            table: "BeerEntries",
            column: "CompetitionCategoryId");

        migrationBuilder.AddForeignKey(
            name: "FK_BeerEntries_CompetitionCategories_CompetitionCategoryId",
            table: "BeerEntries",
            column: "CompetitionCategoryId",
            principalTable: "CompetitionCategories",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_BeerEntries_CompetitionCategories_CompetitionCategoryId",
            table: "BeerEntries");

        migrationBuilder.DropIndex(
            name: "IX_BeerEntries_CompetitionCategoryId",
            table: "BeerEntries");

        migrationBuilder.DropColumn(
            name: "AcceMemberNumber",
            table: "Participants");

        migrationBuilder.DropColumn(
            name: "DateOfBirth",
            table: "Participants");

        migrationBuilder.DropColumn(
            name: "Phone",
            table: "Participants");

        migrationBuilder.DropColumn(
            name: "AbvPercent",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "AcceMemberNumberText",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "BottlingDate",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "BrewDate",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "CategoryText",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "DateOfBirth",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "EntryInstructions",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "Hops",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "Malts",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "OtherIngredients",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "Phone",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "ResolvedCompetitionCategoryId",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "SubmittedAt",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "Yeast",
            table: "ImportRows");

        migrationBuilder.DropColumn(
            name: "AbvPercent",
            table: "BeerEntries");

        migrationBuilder.DropColumn(
            name: "BottlingDate",
            table: "BeerEntries");

        migrationBuilder.DropColumn(
            name: "BrewDate",
            table: "BeerEntries");

        migrationBuilder.DropColumn(
            name: "CompetitionCategoryId",
            table: "BeerEntries");

        migrationBuilder.DropColumn(
            name: "EntryInstructions",
            table: "BeerEntries");

        migrationBuilder.DropColumn(
            name: "Hops",
            table: "BeerEntries");

        migrationBuilder.DropColumn(
            name: "Malts",
            table: "BeerEntries");

        migrationBuilder.DropColumn(
            name: "OtherIngredients",
            table: "BeerEntries");

        migrationBuilder.DropColumn(
            name: "SubmittedAt",
            table: "BeerEntries");

        migrationBuilder.DropColumn(
            name: "Yeast",
            table: "BeerEntries");

        migrationBuilder.AddColumn<string>(
            name: "CollaboratorsJson",
            table: "ImportRows",
            type: "jsonb",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AlterColumn<string>(
            name: "BeerName",
            table: "BeerEntries",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200,
            oldNullable: true);
    }
}
