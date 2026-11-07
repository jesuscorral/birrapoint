using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Errors;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;
using BirraPoint.Api.Modules.Judging.Integration;
using BirraPoint.Api.Modules.Organization.Contracts.Events;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Integration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.Tables.CommonComponents;

/// <summary>
/// Drives <see cref="TableAssignmentApplier.ApplyAsync"/> directly against a real PostgreSQL
/// Testcontainer (no EF InMemory). Covers the rules that used to live in the pure CoiDetector and
/// BosFlagRules classes: conflict-of-interest detection (FR-017) and competition-wide
/// NotValidForBos flag/unflag (FR-018), which Organization applies on the published TableJudgesChanged
/// (routed here to the real handler on the same DbContext). Each test seeds its own competition, so tests
/// are independent.
/// </summary>
public sealed class TableAssignmentApplierTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string StyleCode = "21A";
    private const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(factory.ConnectionString)
        .Options);

    private async Task<Guid> SeedCompetitionAsync()
    {
        await using var db = NewDb();
        var competition = new Competition
        {
            Name = $"Applier {Guid.NewGuid():N}",
            Venue = "Test venue",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 3),
            CreatedByUserId = $"org-{Guid.NewGuid():N}",
        };
        db.Competitions.Add(competition);
        await db.SaveChangesAsync();
        return competition.Id;
    }

    private async Task<Guid> SeedEntryAsync(Guid competitionId, string ownerEmail, params string[] collaboratorEmails)
    {
        await using var db = NewDb();
        // (CompetitionId, Email) is unique: one participant may own several entries.
        var participant = await db.Participants.SingleOrDefaultAsync(p => p.CompetitionId == competitionId && p.Email == ownerEmail);
        if (participant is null)
        {
            participant = new Participant { CompetitionId = competitionId, Name = "Owner", Email = ownerEmail };
            db.Participants.Add(participant);
            await db.SaveChangesAsync();
        }

        var entry = new BeerEntry
        {
            CompetitionId = competitionId,
            ParticipantId = participant.Id,
            BeerName = $"Beer {Guid.NewGuid():N}"[..12],
            StyleCode = StyleCode,
            BlindCode = $"B{Guid.NewGuid():N}"[..8],
        };
        db.BeerEntries.Add(entry);
        await db.SaveChangesAsync();

        foreach (var email in collaboratorEmails)
        {
            db.EntryCollaborators.Add(new EntryCollaborator { BeerEntryId = entry.Id, Email = email });
        }

        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<Guid> SeedJudgeAsync(Guid competitionId, string email)
    {
        await using var db = NewDb();
        var judge = new Judge
        {
            CompetitionId = competitionId,
            Email = email,
            DisplayName = email.Split('@')[0],
            KeycloakUserId = $"kc-{Guid.NewGuid():N}",
        };
        db.Judges.Add(judge);
        await db.SaveChangesAsync();
        return judge.Id;
    }

    private async Task<Guid> SeedTableAsync(Guid competitionId)
    {
        await using var db = NewDb();
        var table = new TastingTable { CompetitionId = competitionId, Name = $"T {Guid.NewGuid():N}"[..10] };
        db.TastingTables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    private async Task SeedActiveTableJudgeAsync(Guid tableId, Guid judgeId)
    {
        await using var db = NewDb();
        db.TableJudges.Add(new TableJudge { TastingTableId = tableId, JudgeId = judgeId });
        await db.SaveChangesAsync();
    }

    private async Task SeedSubmittedEvaluationAsync(Guid tableId, Guid judgeId, Guid beerEntryId)
    {
        await using var db = NewDb();
        db.Evaluations.Add(new Evaluation
        {
            TastingTableId = tableId,
            JudgeId = judgeId,
            BeerEntryId = beerEntryId,
            AromaScore = 10,
            AppearanceScore = 2,
            FlavorScore = 15,
            MouthfeelScore = 4,
            OverallScore = 8,
            AromaComment = LongComment,
            AppearanceComment = LongComment,
            FlavorComment = LongComment,
            MouthfeelComment = LongComment,
            OverallComment = LongComment,
            Status = EvaluationStatus.Confirmed,
            SubmittedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Loads the table fresh (judges + samples), applies the desired end state and saves —
    /// the Applier only stages changes; persisting them is the caller's job (as in the handlers).</summary>
    private async Task<IReadOnlyList<Guid>> ApplyAsync(Guid tableId, Guid[] judgeIds, Guid[] beerEntryIds)
    {
        await using var db = NewDb();
        var table = await db.TastingTables
            .Include(t => t.Judges)
            .Include(t => t.Samples)
            .SingleAsync(t => t.Id == tableId);
        var flaggedEntryIds = await TableAssignmentApplier.ApplyAsync(db, new ConflictOfInterestChecker(db), new InProcessPublisher(db), table, judgeIds, beerEntryIds, CancellationToken.None);
        await db.SaveChangesAsync();
        return flaggedEntryIds;
    }

    private async Task<bool> IsNotValidForBosAsync(Guid entryId)
    {
        await using var db = NewDb();
        return await db.BeerEntries.Where(e => e.Id == entryId).Select(e => e.NotValidForBos).SingleAsync();
    }

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@brew.example";

    private static List<Dictionary<string, object?>> ConflictsOf(JudgingException ex) =>
        Assert.IsType<List<Dictionary<string, object?>>>(ex.Extensions["conflicts"]);

    private async Task AssertNothingPersistedAsync(Guid tableId, params Guid[] entryIds)
    {
        await using var db = NewDb();
        Assert.False(await db.TableJudges.AnyAsync(tj => tj.TastingTableId == tableId));
        Assert.False(await db.TableSamples.AnyAsync(ts => ts.TastingTableId == tableId));
        foreach (var entryId in entryIds)
        {
            Assert.False(await IsNotValidForBosAsync(entryId));
        }
    }

    // ---------------------------------------------------------------- persistence

    [Fact]
    public async Task ApplyStagesChangesWithoutPersistingThem()
    {
        var competitionId = await SeedCompetitionAsync();
        var email = Email("owner");
        var judgeId = await SeedJudgeAsync(competitionId, email);
        var ownedEntryId = await SeedEntryAsync(competitionId, email);
        var tableId = await SeedTableAsync(competitionId);

        await using (var db = NewDb())
        {
            var table = await db.TastingTables
                .Include(t => t.Judges)
                .Include(t => t.Samples)
                .SingleAsync(t => t.Id == tableId, TestContext.Current.CancellationToken);

            // Judge owns ownedEntryId (unassigned), so adding them would flag it for BOS.
            await TableAssignmentApplier.ApplyAsync(db, new ConflictOfInterestChecker(db), new InProcessPublisher(db), table, [judgeId], [], TestContext.Current.CancellationToken);

            Assert.True(db.ChangeTracker.HasChanges());
        }

        await AssertNothingPersistedAsync(tableId, ownedEntryId);
    }

    // ---------------------------------------------------------------- COI (FR-017)

    [Fact]
    public async Task NoConflictWhenNoEmailMatches()
    {
        var competitionId = await SeedCompetitionAsync();
        var judgeId = await SeedJudgeAsync(competitionId, Email("judge"));
        var entryId = await SeedEntryAsync(competitionId, Email("owner"));
        var tableId = await SeedTableAsync(competitionId);

        await ApplyAsync(tableId, [judgeId], [entryId]);

        await using var db = NewDb();
        Assert.True(await db.TableJudges.AnyAsync(tj => tj.TastingTableId == tableId && tj.JudgeId == judgeId, TestContext.Current.CancellationToken));
        Assert.True(await db.TableSamples.AnyAsync(ts => ts.TastingTableId == tableId && ts.BeerEntryId == entryId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConflictWhenJudgeEmailMatchesEntryOwner()
    {
        var competitionId = await SeedCompetitionAsync();
        var email = Email("owner");
        var judgeId = await SeedJudgeAsync(competitionId, email);
        var entryId = await SeedEntryAsync(competitionId, email);
        var tableId = await SeedTableAsync(competitionId);

        var ex = await Assert.ThrowsAsync<JudgingException>(() => ApplyAsync(tableId, [judgeId], [entryId]));

        Assert.Equal(JudgingError.ConflictOfInterest, ex.ErrorType);
        var conflict = Assert.Single(ConflictsOf(ex));
        Assert.Equal(judgeId, conflict["judgeId"]);
        Assert.Equal([entryId], Assert.IsAssignableFrom<IReadOnlyList<Guid>>(conflict["beerEntryIds"]));
        await AssertNothingPersistedAsync(tableId, entryId);
    }

    [Fact]
    public async Task ConflictWhenJudgeEmailMatchesACollaborator()
    {
        var competitionId = await SeedCompetitionAsync();
        var collaboratorEmail = Email("collab");
        var judgeId = await SeedJudgeAsync(competitionId, collaboratorEmail);
        var entryId = await SeedEntryAsync(competitionId, Email("owner"), collaboratorEmail);
        var tableId = await SeedTableAsync(competitionId);

        var ex = await Assert.ThrowsAsync<JudgingException>(() => ApplyAsync(tableId, [judgeId], [entryId]));

        Assert.Equal(JudgingError.ConflictOfInterest, ex.ErrorType);
        var conflict = Assert.Single(ConflictsOf(ex));
        Assert.Equal(judgeId, conflict["judgeId"]);
        Assert.Equal([entryId], Assert.IsAssignableFrom<IReadOnlyList<Guid>>(conflict["beerEntryIds"]));
        await AssertNothingPersistedAsync(tableId, entryId);
    }

    [Fact]
    public async Task MatchingIsCaseInsensitive()
    {
        var competitionId = await SeedCompetitionAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var judgeId = await SeedJudgeAsync(competitionId, $"Owner-{suffix}@Example.com");
        var entryId = await SeedEntryAsync(competitionId, $"owner-{suffix}@example.com");
        var tableId = await SeedTableAsync(competitionId);

        var ex = await Assert.ThrowsAsync<JudgingException>(() => ApplyAsync(tableId, [judgeId], [entryId]));

        Assert.Equal(JudgingError.ConflictOfInterest, ex.ErrorType);
        Assert.Single(ConflictsOf(ex));
        await AssertNothingPersistedAsync(tableId, entryId);
    }

    [Fact]
    public async Task ConflictsAreGroupedPerJudgeAcrossMultipleEntries()
    {
        var competitionId = await SeedCompetitionAsync();
        var email = Email("owner");
        var judgeId = await SeedJudgeAsync(competitionId, email);
        var ownedEntryId = await SeedEntryAsync(competitionId, email);
        var otherEntryId = await SeedEntryAsync(competitionId, Email("someone-else"));
        var tableId = await SeedTableAsync(competitionId);

        var ex = await Assert.ThrowsAsync<JudgingException>(
            () => ApplyAsync(tableId, [judgeId], [ownedEntryId, otherEntryId]));

        var conflict = Assert.Single(ConflictsOf(ex));
        Assert.Equal(judgeId, conflict["judgeId"]);
        Assert.Equal([ownedEntryId], Assert.IsAssignableFrom<IReadOnlyList<Guid>>(conflict["beerEntryIds"]));
        await AssertNothingPersistedAsync(tableId, ownedEntryId, otherEntryId);
    }

    [Fact]
    public async Task MultipleJudgesCanEachProduceTheirOwnConflict()
    {
        var competitionId = await SeedCompetitionAsync();
        var emailA = Email("a");
        var emailB = Email("b");
        var judgeAId = await SeedJudgeAsync(competitionId, emailA);
        var judgeBId = await SeedJudgeAsync(competitionId, emailB);
        var entryAId = await SeedEntryAsync(competitionId, emailA);
        var entryBId = await SeedEntryAsync(competitionId, emailB);
        var tableId = await SeedTableAsync(competitionId);

        var ex = await Assert.ThrowsAsync<JudgingException>(
            () => ApplyAsync(tableId, [judgeAId, judgeBId], [entryAId, entryBId]));

        var conflicts = ConflictsOf(ex);
        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, c =>
            (Guid)c["judgeId"]! == judgeAId
            && ((IReadOnlyList<Guid>)c["beerEntryIds"]!).SequenceEqual([entryAId]));
        Assert.Contains(conflicts, c =>
            (Guid)c["judgeId"]! == judgeBId
            && ((IReadOnlyList<Guid>)c["beerEntryIds"]!).SequenceEqual([entryBId]));
        await AssertNothingPersistedAsync(tableId, entryAId, entryBId);
    }

    // ---------------------------------------------------------------- BOS flag (FR-018)

    [Fact]
    public async Task AddingAJudgeFlagsAllEntriesTheyOwnCompetitionWide()
    {
        var competitionId = await SeedCompetitionAsync();
        var email = Email("judge");
        var judgeId = await SeedJudgeAsync(competitionId, email);
        var ownedUnassignedEntryId = await SeedEntryAsync(competitionId, email);
        var ownedAsCollaboratorEntryId = await SeedEntryAsync(competitionId, Email("owner"), email);
        var otherTableEntryId = await SeedEntryAsync(competitionId, Email("owner"));
        var sampleEntryId = await SeedEntryAsync(competitionId, Email("owner"));
        var otherOwnedEntryId = await SeedEntryAsync(competitionId, email);
        var otherTableId = await SeedTableAsync(competitionId);
        await using (var db = NewDb())
        {
            db.TableSamples.Add(new TableSample { TastingTableId = otherTableId, BeerEntryId = otherOwnedEntryId });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var tableId = await SeedTableAsync(competitionId);

        var flagged = await ApplyAsync(tableId, [judgeId], [sampleEntryId, otherTableEntryId]);

        Assert.Equal(3, flagged.Count);
        Assert.Contains(ownedUnassignedEntryId, flagged);
        Assert.Contains(ownedAsCollaboratorEntryId, flagged);
        Assert.Contains(otherOwnedEntryId, flagged);
        Assert.True(await IsNotValidForBosAsync(ownedUnassignedEntryId));
        Assert.True(await IsNotValidForBosAsync(ownedAsCollaboratorEntryId));
        Assert.True(await IsNotValidForBosAsync(otherOwnedEntryId));
        Assert.False(await IsNotValidForBosAsync(sampleEntryId));
        Assert.False(await IsNotValidForBosAsync(otherTableEntryId));
    }

    [Fact]
    public async Task FlaggingUnionsAcrossMultipleAddedJudgesAndDedups()
    {
        var competitionId = await SeedCompetitionAsync();
        var emailA = Email("a");
        var emailB = Email("b");
        var judgeAId = await SeedJudgeAsync(competitionId, emailA);
        var judgeBId = await SeedJudgeAsync(competitionId, emailB);
        var sharedEntryId = await SeedEntryAsync(competitionId, emailA, emailB);
        var soloEntryId = await SeedEntryAsync(competitionId, emailA);
        var sampleEntryId = await SeedEntryAsync(competitionId, Email("owner"));
        var tableId = await SeedTableAsync(competitionId);

        var flagged = await ApplyAsync(tableId, [judgeAId, judgeBId], [sampleEntryId]);

        Assert.Equal(2, flagged.Count);
        Assert.Contains(sharedEntryId, flagged);
        Assert.Contains(soloEntryId, flagged);
    }

    [Fact]
    public async Task JudgeOwningNoEntriesFlagsNothing()
    {
        var competitionId = await SeedCompetitionAsync();
        var judgeId = await SeedJudgeAsync(competitionId, Email("judge"));
        var entryId = await SeedEntryAsync(competitionId, Email("owner"));
        var tableId = await SeedTableAsync(competitionId);

        var flagged = await ApplyAsync(tableId, [judgeId], [entryId]);

        Assert.Empty(flagged);
        Assert.False(await IsNotValidForBosAsync(entryId));
    }

    // ---------------------------------------------------------------- BOS unflag (FR-018)

    /// <summary>Judge owns an entry, is added to a table (flagging it); returns the ids for removal tests.</summary>
    private async Task<(Guid CompetitionId, Guid JudgeId, Guid OwnedEntryId, Guid SampleEntryId, Guid TableId)>
        SeedFlaggedJudgeOnTableAsync()
    {
        var competitionId = await SeedCompetitionAsync();
        var email = Email("judge");
        var judgeId = await SeedJudgeAsync(competitionId, email);
        var ownedEntryId = await SeedEntryAsync(competitionId, email);
        var sampleEntryId = await SeedEntryAsync(competitionId, Email("owner"));
        var tableId = await SeedTableAsync(competitionId);

        var flagged = await ApplyAsync(tableId, [judgeId], [sampleEntryId]);
        Assert.Equal([ownedEntryId], flagged);
        Assert.True(await IsNotValidForBosAsync(ownedEntryId));

        return (competitionId, judgeId, ownedEntryId, sampleEntryId, tableId);
    }

    [Fact]
    public async Task RemovingAJudgeUnflagsTheirEntriesWhenNoOtherAssignmentAndNoEvaluation()
    {
        var s = await SeedFlaggedJudgeOnTableAsync();

        var flagged = await ApplyAsync(s.TableId, [], [s.SampleEntryId]);

        Assert.Empty(flagged);
        Assert.False(await IsNotValidForBosAsync(s.OwnedEntryId));
    }

    [Fact]
    public async Task RemovingAJudgeDoesNotUnflagWhenStillAssignedToAnotherTable()
    {
        var s = await SeedFlaggedJudgeOnTableAsync();
        var otherTableId = await SeedTableAsync(s.CompetitionId);
        await SeedActiveTableJudgeAsync(otherTableId, s.JudgeId);

        await ApplyAsync(s.TableId, [], [s.SampleEntryId]);

        Assert.True(await IsNotValidForBosAsync(s.OwnedEntryId));
    }

    /// <summary>
    /// The permanence rule (FR-018): once a judge has submitted any evaluation, the flag can never be
    /// lifted — even after they lose every table assignment.
    /// </summary>
    [Fact]
    public async Task RemovingAJudgeDoesNotUnflagOnceTheyHaveSubmittedAnyEvaluation()
    {
        var s = await SeedFlaggedJudgeOnTableAsync();
        await SeedSubmittedEvaluationAsync(s.TableId, s.JudgeId, s.SampleEntryId);

        await ApplyAsync(s.TableId, [], [s.SampleEntryId]);

        Assert.True(await IsNotValidForBosAsync(s.OwnedEntryId));
    }

    [Fact]
    public async Task RemovingAJudgeDoesNotUnflagWhenStillAssignedElsewhereAndHasEvaluated()
    {
        var s = await SeedFlaggedJudgeOnTableAsync();
        var otherTableId = await SeedTableAsync(s.CompetitionId);
        await SeedActiveTableJudgeAsync(otherTableId, s.JudgeId);
        await SeedSubmittedEvaluationAsync(s.TableId, s.JudgeId, s.SampleEntryId);

        await ApplyAsync(s.TableId, [], [s.SampleEntryId]);

        Assert.True(await IsNotValidForBosAsync(s.OwnedEntryId));
    }

    /// <summary>Routes TableJudgesChanged to Organization's real handler on the applier's DbContext, as
    /// MediatR does in the app (same scope, same unit of work).</summary>
    private sealed class InProcessPublisher(AppDbContext db) : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Publish((INotification)notification, cancellationToken);

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            notification is TableJudgesChanged changed
                ? new UpdateBosFlagsOnTableJudgesChanged(db, new ConflictOfInterestChecker(db), new JudgeEngagement(db))
                    .Handle(changed, cancellationToken)
                : throw new InvalidOperationException($"Unexpected notification {notification.GetType().Name}.");
    }
}
