using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOrganizers : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "OrganizerId",
            table: "Competitions",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "Organizers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                KeycloakUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                FirstName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                LastName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Organizers", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Competitions_OrganizerId",
            table: "Competitions",
            column: "OrganizerId");

        migrationBuilder.CreateIndex(
            name: "IX_Organizers_Email",
            table: "Organizers",
            column: "Email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Organizers_KeycloakUserId",
            table: "Organizers",
            column: "KeycloakUserId",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_Competitions_Organizers_OrganizerId",
            table: "Competitions",
            column: "OrganizerId",
            principalTable: "Organizers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Competitions_Organizers_OrganizerId",
            table: "Competitions");

        migrationBuilder.DropTable(
            name: "Organizers");

        migrationBuilder.DropIndex(
            name: "IX_Competitions_OrganizerId",
            table: "Competitions");

        migrationBuilder.DropColumn(
            name: "OrganizerId",
            table: "Competitions");
    }
}
