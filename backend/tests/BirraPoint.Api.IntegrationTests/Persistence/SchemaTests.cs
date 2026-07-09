using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BirraPoint.Api.IntegrationTests.Persistence;

/// <summary>
/// One PostgreSQL 16 container per test class; the InitialCreate migration is applied once.
/// Tests isolate their data with fresh Guids/codes instead of per-test databases.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public DbContextOptions<AppDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using var db = new AppDbContext(Options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// Schema-level invariants of T009: the InitialCreate migration applies cleanly and the
/// database enforces the constraints named in data-model.md (computed Total, idempotency
/// unique index, date check constraint, partial unique index for open discrepancy alerts).
/// </summary>
public sealed class SchemaTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private AppDbContext NewContext() => new(fixture.Options);

    [Fact]
    public async Task Migration_applies_cleanly_and_creates_the_schema()
    {
        await using var db = NewContext();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);

        // Spot-check that the core tables exist and are queryable (the fixture's database
        // is shared across this class's tests, so row counts here are not assumed to be zero).
        await db.Evaluations.CountAsync();
        await db.Competitions.CountAsync();
        await db.AuditLogs.CountAsync();
    }

    [Fact]
    public async Task Evaluation_total_is_computed_by_the_database()
    {
        await using var db = NewContext();
        var (_, judge, entry, table) = await SeedTableGraphAsync(db);

        var evaluation = NewEvaluation(table.Id, judge.Id, entry.Id,
            aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8);
        db.Evaluations.Add(evaluation);
        await db.SaveChangesAsync();

        await using var verify = NewContext();
        var stored = await verify.Evaluations.AsNoTracking().SingleAsync(e => e.Id == evaluation.Id);
        Assert.Equal(39, stored.Total);
    }

    [Fact]
    public async Task Duplicate_evaluation_for_same_judge_and_entry_is_rejected()
    {
        await using var db = NewContext();
        var (_, judge, entry, table) = await SeedTableGraphAsync(db);

        db.Evaluations.Add(NewEvaluation(table.Id, judge.Id, entry.Id));
        await db.SaveChangesAsync();

        await using var second = NewContext();
        second.Evaluations.Add(NewEvaluation(table.Id, judge.Id, entry.Id));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
    }

    [Fact]
    public async Task Competition_with_end_date_before_start_date_is_rejected()
    {
        await using var db = NewContext();
        var competition = NewCompetition();
        competition.StartDate = new DateOnly(2026, 9, 2);
        competition.EndDate = new DateOnly(2026, 9, 1);

        db.Competitions.Add(competition);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, pg.SqlState);
    }

    [Fact]
    public async Task Only_one_open_discrepancy_alert_per_table_and_entry()
    {
        await using var db = NewContext();
        var (_, _, entry, table) = await SeedTableGraphAsync(db);

        // A resolved alert plus an open one may coexist for the same (table, entry)…
        db.DiscrepancyAlerts.Add(new DiscrepancyAlert
        {
            TastingTableId = table.Id,
            BeerEntryId = entry.Id,
            Status = DiscrepancyStatus.Resolved,
            ResolvedAt = DateTimeOffset.UtcNow,
        });
        db.DiscrepancyAlerts.Add(new DiscrepancyAlert
        {
            TastingTableId = table.Id,
            BeerEntryId = entry.Id,
            Status = DiscrepancyStatus.Open,
        });
        await db.SaveChangesAsync();

        // …but a second open alert violates the partial unique index.
        await using var second = NewContext();
        second.DiscrepancyAlerts.Add(new DiscrepancyAlert
        {
            TastingTableId = table.Id,
            BeerEntryId = entry.Id,
            Status = DiscrepancyStatus.Open,
        });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        var pg = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, pg.SqlState);
    }

    [Fact]
    public async Task Timestamps_are_stamped_on_insert_and_update()
    {
        await using var db = NewContext();
        var before = DateTimeOffset.UtcNow;

        var competition = NewCompetition();
        db.Competitions.Add(competition);
        await db.SaveChangesAsync();

        Assert.InRange(competition.CreatedAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(competition.CreatedAt, competition.UpdatedAt);

        await Task.Delay(10);
        competition.Name = "Renamed";
        await db.SaveChangesAsync();

        Assert.True(competition.UpdatedAt > competition.CreatedAt);
    }

    private static Competition NewCompetition() => new()
    {
        Name = "Cata Norte 2026",
        Venue = "Bilbao",
        StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2026, 9, 2),
        State = CompetitionState.Draft,
        CreatedByUserId = "kc-organizer-1",
    };

    private static Evaluation NewEvaluation(Guid tableId, Guid judgeId, Guid entryId,
        int aroma = 8, int appearance = 2, int flavor = 14, int mouthfeel = 3, int overall = 7) => new()
        {
            TastingTableId = tableId,
            JudgeId = judgeId,
            BeerEntryId = entryId,
            AromaScore = aroma,
            AppearanceScore = appearance,
            FlavorScore = flavor,
            MouthfeelScore = mouthfeel,
            OverallScore = overall,
            AromaComment = "Citrus and pine hop aroma, moderate intensity.",
            AppearanceComment = "Deep golden, persistent white head, brilliant.",
            FlavorComment = "Balanced malt backbone with resinous hop finish.",
            MouthfeelComment = "Medium body, lively carbonation, dry finish.",
            OverallComment = "A clean, well-executed example of the style.",
            Status = EvaluationStatus.Confirmed,
            SubmittedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>Seeds competition → style → participant → entry → judge → table (+membership/sample).</summary>
    private static async Task<(Competition Competition, Judge Judge, BeerEntry Entry, TastingTable Table)>
        SeedTableGraphAsync(AppDbContext db)
    {
        var competition = NewCompetition();

        var style = new BjcpStyle
        {
            Code = Guid.NewGuid().ToString("N")[..5],
            Name = "American IPA",
            CategoryNumber = "21",
            CategoryName = "IPA",
            DescriptionJson = "{}",
        };

        var participant = new Participant
        {
            CompetitionId = competition.Id,
            Name = "Brewer One",
            Email = $"brewer-{Guid.NewGuid():N}@example.test",
        };

        var entry = new BeerEntry
        {
            CompetitionId = competition.Id,
            ParticipantId = participant.Id,
            BeerName = "Hop Cannon",
            StyleCode = style.Code,
            BlindCode = Guid.NewGuid().ToString("N")[..8],
        };

        var judge = new Judge
        {
            CompetitionId = competition.Id,
            Email = $"judge-{Guid.NewGuid():N}@example.test",
            DisplayName = "Judge One",
        };

        var table = new TastingTable
        {
            CompetitionId = competition.Id,
            Name = $"Table {Guid.NewGuid().ToString("N")[..6]}",
            State = TableState.Open,
        };

        db.AddRange(competition, style, participant, entry, judge, table);
        db.Add(new TableJudge { TastingTableId = table.Id, JudgeId = judge.Id });
        db.Add(new TableSample { TastingTableId = table.Id, BeerEntryId = entry.Id, SequenceOrder = 1 });
        await db.SaveChangesAsync();

        return (competition, judge, entry, table);
    }
}
