using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Modules.Judging.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "judging");

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "judging",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Handler = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_judging_InboxMessages", x => new { x.EventId, x.Handler });
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "judging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_judging_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TastingTables",
                schema: "judging",
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
                });

            migrationBuilder.CreateTable(
                name: "DiscrepancyAlerts",
                schema: "judging",
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
                        name: "FK_DiscrepancyAlerts_TastingTables_TastingTableId",
                        column: x => x.TastingTableId,
                        principalSchema: "judging",
                        principalTable: "TastingTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Evaluations",
                schema: "judging",
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
                    DescriptorsJson = table.Column<string>(type: "jsonb", nullable: true),
                    FeedbackComment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
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
                        name: "FK_Evaluations_TastingTables_TastingTableId",
                        column: x => x.TastingTableId,
                        principalSchema: "judging",
                        principalTable: "TastingTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TableJudges",
                schema: "judging",
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
                        name: "FK_TableJudges_TastingTables_TastingTableId",
                        column: x => x.TastingTableId,
                        principalSchema: "judging",
                        principalTable: "TastingTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TableSamples",
                schema: "judging",
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
                        name: "FK_TableSamples_TastingTables_TastingTableId",
                        column: x => x.TastingTableId,
                        principalSchema: "judging",
                        principalTable: "TastingTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscrepancyAlerts_BeerEntryId",
                schema: "judging",
                table: "DiscrepancyAlerts",
                column: "BeerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscrepancyAlerts_TastingTableId_BeerEntryId",
                schema: "judging",
                table: "DiscrepancyAlerts",
                columns: new[] { "TastingTableId", "BeerEntryId" },
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_BeerEntryId",
                schema: "judging",
                table: "Evaluations",
                column: "BeerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_JudgeId_BeerEntryId",
                schema: "judging",
                table: "Evaluations",
                columns: new[] { "JudgeId", "BeerEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Evaluations_TastingTableId",
                schema: "judging",
                table: "Evaluations",
                column: "TastingTableId");

            migrationBuilder.CreateIndex(
                name: "IX_judging_InboxMessages_ProcessedAt",
                schema: "judging",
                table: "InboxMessages",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_judging_OutboxMessages_Status_LeaseExpiresAt",
                schema: "judging",
                table: "OutboxMessages",
                columns: new[] { "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_judging_OutboxMessages_Status_NextAttemptAt",
                schema: "judging",
                table: "OutboxMessages",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TableJudges_JudgeId",
                schema: "judging",
                table: "TableJudges",
                column: "JudgeId");

            migrationBuilder.CreateIndex(
                name: "IX_TableSamples_BeerEntryId",
                schema: "judging",
                table: "TableSamples",
                column: "BeerEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TableSamples_TastingTableId_SequenceOrder",
                schema: "judging",
                table: "TableSamples",
                columns: new[] { "TastingTableId", "SequenceOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TastingTables_CompetitionId_Name",
                schema: "judging",
                table: "TastingTables",
                columns: new[] { "CompetitionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TastingTables_OrderFixedByJudgeId",
                schema: "judging",
                table: "TastingTables",
                column: "OrderFixedByJudgeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscrepancyAlerts",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "Evaluations",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "TableJudges",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "TableSamples",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "TastingTables",
                schema: "judging");
        }
    }
}
