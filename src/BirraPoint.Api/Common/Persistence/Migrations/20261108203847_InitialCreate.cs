using System;
using System.Text.Json;
using BirraPoint.Api.Modules.Catalog.Persistence.Seeding;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BirraPoint.Api.Common.Persistence.Migrations
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
                name: "audit");

            migrationBuilder.EnsureSchema(
                name: "organization");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "judging");

            migrationBuilder.EnsureSchema(
                name: "jobs");

            migrationBuilder.EnsureSchema(
                name: "publication");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

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
                name: "DispatchJobs",
                schema: "jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchJobs_Competitions_CompetitionId",
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
                    table.ForeignKey(
                        name: "FK_ResultsArchives_Competitions_CompetitionId",
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
                        name: "FK_CompetitionCategoryStyles_BjcpStyles_StyleCode",
                        column: x => x.StyleCode,
                        principalSchema: "catalog",
                        principalTable: "BjcpStyles",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
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
                    table.ForeignKey(
                        name: "FK_TastingTables_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalSchema: "organization",
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TastingTables_Judges_OrderFixedByJudgeId",
                        column: x => x.OrderFixedByJudgeId,
                        principalSchema: "organization",
                        principalTable: "Judges",
                        principalColumn: "Id");
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
                        name: "FK_BeerEntries_BjcpStyles_StyleCode",
                        column: x => x.StyleCode,
                        principalSchema: "catalog",
                        principalTable: "BjcpStyles",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
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
                        name: "FK_TableJudges_Judges_JudgeId",
                        column: x => x.JudgeId,
                        principalSchema: "organization",
                        principalTable: "Judges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TableJudges_TastingTables_TastingTableId",
                        column: x => x.TastingTableId,
                        principalSchema: "judging",
                        principalTable: "TastingTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                        name: "FK_DiscrepancyAlerts_BeerEntries_BeerEntryId",
                        column: x => x.BeerEntryId,
                        principalSchema: "organization",
                        principalTable: "BeerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiscrepancyAlerts_TastingTables_TastingTableId",
                        column: x => x.TastingTableId,
                        principalSchema: "judging",
                        principalTable: "TastingTables",
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
                        name: "FK_Evaluations_BeerEntries_BeerEntryId",
                        column: x => x.BeerEntryId,
                        principalSchema: "organization",
                        principalTable: "BeerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Evaluations_Judges_JudgeId",
                        column: x => x.JudgeId,
                        principalSchema: "organization",
                        principalTable: "Judges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Evaluations_TastingTables_TastingTableId",
                        column: x => x.TastingTableId,
                        principalSchema: "judging",
                        principalTable: "TastingTables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    table.ForeignKey(
                        name: "FK_GeneratedScoreSheets_BeerEntries_BeerEntryId",
                        column: x => x.BeerEntryId,
                        principalSchema: "organization",
                        principalTable: "BeerEntries",
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
                        name: "FK_TableSamples_BeerEntries_BeerEntryId",
                        column: x => x.BeerEntryId,
                        principalSchema: "organization",
                        principalTable: "BeerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TableSamples_TastingTables_TastingTableId",
                        column: x => x.TastingTableId,
                        principalSchema: "judging",
                        principalTable: "TastingTables",
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
                name: "IX_DispatchJobs_CompetitionId",
                schema: "jobs",
                table: "DispatchJobs",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchJobs_Status_LeaseExpiresAt",
                schema: "jobs",
                table: "DispatchJobs",
                columns: new[] { "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DispatchJobs_Status_NextAttemptAt",
                schema: "jobs",
                table: "DispatchJobs",
                columns: new[] { "Status", "NextAttemptAt" });

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
                name: "IX_GeneratedScoreSheets_BeerEntryId",
                schema: "publication",
                table: "GeneratedScoreSheets",
                column: "BeerEntryId",
                unique: true);

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
                name: "IX_Participants_CompetitionId_Email",
                schema: "organization",
                table: "Participants",
                columns: new[] { "CompetitionId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResultsArchives_CompetitionId",
                schema: "publication",
                table: "ResultsArchives",
                column: "CompetitionId",
                unique: true);

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

            migrationBuilder.InsertData(schema: DbSchemas.Catalog, table: "BjcpStyles", columns: columns, values: values);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "CompetitionCategoryStyles",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "DiscrepancyAlerts",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "DispatchJobs",
                schema: "jobs");

            migrationBuilder.DropTable(
                name: "EntryCollaborators",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Evaluations",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "GeneratedScoreSheets",
                schema: "publication");

            migrationBuilder.DropTable(
                name: "ImportRows",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Invitations",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "JudgeImportRows",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "ResultsArchives",
                schema: "publication");

            migrationBuilder.DropTable(
                name: "TableJudges",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "TableSamples",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "ImportBatches",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "JudgeImportBatches",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "BeerEntries",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "TastingTables",
                schema: "judging");

            migrationBuilder.DropTable(
                name: "BjcpStyles",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "CompetitionCategories",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Participants",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "Judges",
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
