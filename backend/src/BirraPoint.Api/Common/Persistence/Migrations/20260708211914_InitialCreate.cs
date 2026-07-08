using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                EntityId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DataJson = table.Column<string>(type: "jsonb", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLogs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "BjcpStyles",
            columns: table => new
            {
                Code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CategoryNumber = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                CategoryName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BjcpStyles", x => x.Code);
            });

        migrationBuilder.CreateTable(
            name: "Competitions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Venue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                EntryLimit = table.Column<int>(type: "integer", nullable: true),
                StartRegistration = table.Column<DateOnly>(type: "date", nullable: true),
                EndRegistration = table.Column<DateOnly>(type: "date", nullable: true),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Competitions", x => x.Id);
                table.CheckConstraint("CK_Competitions_EndDate", "\"EndDate\" >= \"StartDate\"");
                table.CheckConstraint("CK_Competitions_EntryLimit", "\"EntryLimit\" IS NULL OR \"EntryLimit\" > 0");
                table.CheckConstraint("CK_Competitions_RegistrationWindow", "\"StartRegistration\" IS NULL OR \"EndRegistration\" IS NULL OR \"EndRegistration\" >= \"StartRegistration\"");
            });

        migrationBuilder.CreateTable(
            name: "DispatchJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DispatchJobs", x => x.Id);
                table.ForeignKey(
                    name: "FK_DispatchJobs_Competitions_CompetitionId",
                    column: x => x.CompetitionId,
                    principalTable: "Competitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Judges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                KeycloakUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Judges", x => x.Id);
                table.ForeignKey(
                    name: "FK_Judges_Competitions_CompetitionId",
                    column: x => x.CompetitionId,
                    principalTable: "Competitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Participants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Participants", x => x.Id);
                table.ForeignKey(
                    name: "FK_Participants_Competitions_CompetitionId",
                    column: x => x.CompetitionId,
                    principalTable: "Competitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Invitations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                JudgeId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Invitations", x => x.Id);
                table.ForeignKey(
                    name: "FK_Invitations_Judges_JudgeId",
                    column: x => x.JudgeId,
                    principalTable: "Judges",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TastingTables",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                OrderFixedByJudgeId = table.Column<Guid>(type: "uuid", nullable: true),
                OrderFixedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TastingTables", x => x.Id);
                table.ForeignKey(
                    name: "FK_TastingTables_Competitions_CompetitionId",
                    column: x => x.CompetitionId,
                    principalTable: "Competitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TastingTables_Judges_OrderFixedByJudgeId",
                    column: x => x.OrderFixedByJudgeId,
                    principalTable: "Judges",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "BeerEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                BeerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                StyleCode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                BlindCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                NotValidForBos = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BeerEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_BeerEntries_BjcpStyles_StyleCode",
                    column: x => x.StyleCode,
                    principalTable: "BjcpStyles",
                    principalColumn: "Code",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_BeerEntries_Competitions_CompetitionId",
                    column: x => x.CompetitionId,
                    principalTable: "Competitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_BeerEntries_Participants_ParticipantId",
                    column: x => x.ParticipantId,
                    principalTable: "Participants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TableJudges",
            columns: table => new
            {
                TastingTableId = table.Column<Guid>(type: "uuid", nullable: false),
                JudgeId = table.Column<Guid>(type: "uuid", nullable: false),
                RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TableJudges", x => new { x.TastingTableId, x.JudgeId });
                table.ForeignKey(
                    name: "FK_TableJudges_Judges_JudgeId",
                    column: x => x.JudgeId,
                    principalTable: "Judges",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TableJudges_TastingTables_TastingTableId",
                    column: x => x.TastingTableId,
                    principalTable: "TastingTables",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "DiscrepancyAlerts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TastingTableId = table.Column<Guid>(type: "uuid", nullable: false),
                BeerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DiscrepancyAlerts", x => x.Id);
                table.ForeignKey(
                    name: "FK_DiscrepancyAlerts_BeerEntries_BeerEntryId",
                    column: x => x.BeerEntryId,
                    principalTable: "BeerEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DiscrepancyAlerts_TastingTables_TastingTableId",
                    column: x => x.TastingTableId,
                    principalTable: "TastingTables",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EntryCollaborators",
            columns: table => new
            {
                BeerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EntryCollaborators", x => new { x.BeerEntryId, x.Email });
                table.ForeignKey(
                    name: "FK_EntryCollaborators_BeerEntries_BeerEntryId",
                    column: x => x.BeerEntryId,
                    principalTable: "BeerEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Evaluations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TastingTableId = table.Column<Guid>(type: "uuid", nullable: false),
                JudgeId = table.Column<Guid>(type: "uuid", nullable: false),
                BeerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                AromaScore = table.Column<int>(type: "integer", nullable: false),
                AppearanceScore = table.Column<int>(type: "integer", nullable: false),
                FlavorScore = table.Column<int>(type: "integer", nullable: false),
                MouthfeelScore = table.Column<int>(type: "integer", nullable: false),
                OverallScore = table.Column<int>(type: "integer", nullable: false),
                AromaComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                AppearanceComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                FlavorComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                MouthfeelComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                OverallComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                Total = table.Column<int>(type: "integer", nullable: false, computedColumnSql: "\"AromaScore\" + \"AppearanceScore\" + \"FlavorScore\" + \"MouthfeelScore\" + \"OverallScore\"", stored: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Evaluations", x => x.Id);
                table.ForeignKey(
                    name: "FK_Evaluations_BeerEntries_BeerEntryId",
                    column: x => x.BeerEntryId,
                    principalTable: "BeerEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Evaluations_Judges_JudgeId",
                    column: x => x.JudgeId,
                    principalTable: "Judges",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_Evaluations_TastingTables_TastingTableId",
                    column: x => x.TastingTableId,
                    principalTable: "TastingTables",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "TableSamples",
            columns: table => new
            {
                TastingTableId = table.Column<Guid>(type: "uuid", nullable: false),
                BeerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                SequenceOrder = table.Column<int>(type: "integer", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TableSamples", x => new { x.TastingTableId, x.BeerEntryId });
                table.ForeignKey(
                    name: "FK_TableSamples_BeerEntries_BeerEntryId",
                    column: x => x.BeerEntryId,
                    principalTable: "BeerEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TableSamples_TastingTables_TastingTableId",
                    column: x => x.TastingTableId,
                    principalTable: "TastingTables",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BeerEntries_CompetitionId_BlindCode",
            table: "BeerEntries",
            columns: new[] { "CompetitionId", "BlindCode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_BeerEntries_ParticipantId",
            table: "BeerEntries",
            column: "ParticipantId");

        migrationBuilder.CreateIndex(
            name: "IX_BeerEntries_StyleCode",
            table: "BeerEntries",
            column: "StyleCode");

        migrationBuilder.CreateIndex(
            name: "IX_DiscrepancyAlerts_BeerEntryId",
            table: "DiscrepancyAlerts",
            column: "BeerEntryId");

        migrationBuilder.CreateIndex(
            name: "IX_DiscrepancyAlerts_TastingTableId_BeerEntryId",
            table: "DiscrepancyAlerts",
            columns: new[] { "TastingTableId", "BeerEntryId" },
            unique: true,
            filter: "\"Status\" = 'Open'");

        migrationBuilder.CreateIndex(
            name: "IX_DispatchJobs_CompetitionId",
            table: "DispatchJobs",
            column: "CompetitionId");

        migrationBuilder.CreateIndex(
            name: "IX_Evaluations_BeerEntryId",
            table: "Evaluations",
            column: "BeerEntryId");

        migrationBuilder.CreateIndex(
            name: "IX_Evaluations_JudgeId_BeerEntryId",
            table: "Evaluations",
            columns: new[] { "JudgeId", "BeerEntryId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Evaluations_TastingTableId",
            table: "Evaluations",
            column: "TastingTableId");

        migrationBuilder.CreateIndex(
            name: "IX_Invitations_JudgeId",
            table: "Invitations",
            column: "JudgeId");

        migrationBuilder.CreateIndex(
            name: "IX_Judges_CompetitionId_Email",
            table: "Judges",
            columns: new[] { "CompetitionId", "Email" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Participants_CompetitionId_Email",
            table: "Participants",
            columns: new[] { "CompetitionId", "Email" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TableJudges_JudgeId",
            table: "TableJudges",
            column: "JudgeId");

        migrationBuilder.CreateIndex(
            name: "IX_TableSamples_BeerEntryId",
            table: "TableSamples",
            column: "BeerEntryId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TableSamples_TastingTableId_SequenceOrder",
            table: "TableSamples",
            columns: new[] { "TastingTableId", "SequenceOrder" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TastingTables_CompetitionId_Name",
            table: "TastingTables",
            columns: new[] { "CompetitionId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TastingTables_OrderFixedByJudgeId",
            table: "TastingTables",
            column: "OrderFixedByJudgeId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AuditLogs");

        migrationBuilder.DropTable(
            name: "DiscrepancyAlerts");

        migrationBuilder.DropTable(
            name: "DispatchJobs");

        migrationBuilder.DropTable(
            name: "EntryCollaborators");

        migrationBuilder.DropTable(
            name: "Evaluations");

        migrationBuilder.DropTable(
            name: "Invitations");

        migrationBuilder.DropTable(
            name: "TableJudges");

        migrationBuilder.DropTable(
            name: "TableSamples");

        migrationBuilder.DropTable(
            name: "BeerEntries");

        migrationBuilder.DropTable(
            name: "TastingTables");

        migrationBuilder.DropTable(
            name: "BjcpStyles");

        migrationBuilder.DropTable(
            name: "Participants");

        migrationBuilder.DropTable(
            name: "Judges");

        migrationBuilder.DropTable(
            name: "Competitions");
    }
}
