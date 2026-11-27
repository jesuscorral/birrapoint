using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Modules.Organization.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organization");

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "organization",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Handler = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_InboxMessages", x => new { x.EventId, x.Handler });
                });

            migrationBuilder.CreateTable(
                name: "Organizers",
                schema: "organization",
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

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "organization",
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
                    table.PrimaryKey("PK_organization_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Competitions",
                schema: "organization",
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
                    OrganizerId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitions", x => x.Id);
                    table.CheckConstraint("CK_Competitions_EndDate", "\"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("CK_Competitions_EntryLimit", "\"EntryLimit\" IS NULL OR \"EntryLimit\" > 0");
                    table.CheckConstraint("CK_Competitions_RegistrationWindow", "\"StartRegistration\" IS NULL OR \"EndRegistration\" IS NULL OR \"EndRegistration\" >= \"StartRegistration\"");
                    table.ForeignKey(
                        name: "FK_Competitions_Organizers_OrganizerId",
                        column: x => x.OrganizerId,
                        principalSchema: "organization",
                        principalTable: "Organizers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionCategories",
                schema: "organization",
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
                        principalSchema: "organization",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                schema: "organization",
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
                        principalSchema: "organization",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JudgeImportBatches",
                schema: "organization",
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
                        principalSchema: "organization",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Judges",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    KeycloakUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BjcpRank = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BjcpId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PreferredCategory = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Preferences = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Judges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Judges_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "organization",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Participants",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    AcceMemberNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Participants_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "organization",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionCategoryStyles",
                schema: "organization",
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
                        name: "FK_CompetitionCategoryStyles_CompetitionCategories_Competition~",
                        column: x => x.CompetitionCategoryId,
                        principalSchema: "organization",
                        principalTable: "CompetitionCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompetitionCategoryStyles_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "organization",
                        principalTable: "Competitions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ImportRows",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    ParticipantName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ParticipantEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    AcceMemberNumberText = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CategoryText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResolvedCompetitionCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    StyleText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResolvedStyleCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AbvPercent = table.Column<decimal>(type: "numeric(4,2)", nullable: true),
                    BrewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BottlingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Malts = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Hops = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Yeast = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OtherIngredients = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EntryInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BeerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                        principalSchema: "organization",
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JudgeImportRows",
                schema: "organization",
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
                        principalSchema: "organization",
                        principalTable: "JudgeImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                schema: "organization",
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
                        principalSchema: "organization",
                        principalTable: "Judges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BeerEntries",
                schema: "organization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BeerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    StyleCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BlindCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CompetitionCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AbvPercent = table.Column<decimal>(type: "numeric(4,2)", nullable: false),
                    BrewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BottlingDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Malts = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Hops = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Yeast = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OtherIngredients = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EntryInstructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NotValidForBos = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BeerEntries_CompetitionCategories_CompetitionCategoryId",
                        column: x => x.CompetitionCategoryId,
                        principalSchema: "organization",
                        principalTable: "CompetitionCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BeerEntries_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "organization",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BeerEntries_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalSchema: "organization",
                        principalTable: "Participants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EntryCollaborators",
                schema: "organization",
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
                        principalSchema: "organization",
                        principalTable: "BeerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BeerEntries_CompetitionCategoryId",
                schema: "organization",
                table: "BeerEntries",
                column: "CompetitionCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BeerEntries_CompetitionId_BlindCode",
                schema: "organization",
                table: "BeerEntries",
                columns: new[] { "CompetitionId", "BlindCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BeerEntries_ParticipantId",
                schema: "organization",
                table: "BeerEntries",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_BeerEntries_StyleCode",
                schema: "organization",
                table: "BeerEntries",
                column: "StyleCode");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionCategories_CompetitionId_Name",
                schema: "organization",
                table: "CompetitionCategories",
                columns: new[] { "CompetitionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionCategoryStyles_CompetitionId_StyleCode",
                schema: "organization",
                table: "CompetitionCategoryStyles",
                columns: new[] { "CompetitionId", "StyleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionCategoryStyles_StyleCode",
                schema: "organization",
                table: "CompetitionCategoryStyles",
                column: "StyleCode");

            migrationBuilder.CreateIndex(
                name: "IX_Competitions_OrganizerId",
                schema: "organization",
                table: "Competitions",
                column: "OrganizerId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CompetitionId",
                schema: "organization",
                table: "ImportBatches",
                column: "CompetitionId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRows_ImportBatchId_RowNumber",
                schema: "organization",
                table: "ImportRows",
                columns: new[] { "ImportBatchId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_InboxMessages_ProcessedAt",
                schema: "organization",
                table: "InboxMessages",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_JudgeId",
                schema: "organization",
                table: "Invitations",
                column: "JudgeId");

            migrationBuilder.CreateIndex(
                name: "IX_JudgeImportBatches_CompetitionId",
                schema: "organization",
                table: "JudgeImportBatches",
                column: "CompetitionId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_JudgeImportRows_JudgeImportBatchId_RowNumber",
                schema: "organization",
                table: "JudgeImportRows",
                columns: new[] { "JudgeImportBatchId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Judges_CompetitionId_Email",
                schema: "organization",
                table: "Judges",
                columns: new[] { "CompetitionId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizers_Email",
                schema: "organization",
                table: "Organizers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizers_KeycloakUserId",
                schema: "organization",
                table: "Organizers",
                column: "KeycloakUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_OutboxMessages_Status_LeaseExpiresAt",
                schema: "organization",
                table: "OutboxMessages",
                columns: new[] { "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_organization_OutboxMessages_Status_NextAttemptAt",
                schema: "organization",
                table: "OutboxMessages",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Participants_CompetitionId_Email",
                schema: "organization",
                table: "Participants",
                columns: new[] { "CompetitionId", "Email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetitionCategoryStyles",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "EntryCollaborators",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "ImportRows",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Invitations",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "JudgeImportRows",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "BeerEntries",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "ImportBatches",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Judges",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "JudgeImportBatches",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "CompetitionCategories",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Participants",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Competitions",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Organizers",
                schema: "organization");
        }
    }
}
