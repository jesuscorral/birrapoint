using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.IntegrationTests.Auth;

/// <summary>
/// T023: JudgeResolver backfills Judge.KeycloakUserId (and DisplayName, when a `name` claim is
/// present) the first time a judge authenticates, matching every Judge row across competitions
/// that shares their email — the COI key (CompetitionId, Email) is not globally unique, so one
/// person can judge several competitions.
/// </summary>
public sealed class JudgeResolverTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private AppDbContext NewContext() => new(fixture.Options);

    [Fact]
    public async Task Backfills_keycloak_user_id_and_display_name_on_every_judge_row_sharing_the_email()
    {
        var email = $"judge-{Guid.NewGuid():N}@example.test";
        var competitionA = NewCompetition();
        var competitionB = NewCompetition();
        var judgeA = NewJudge(competitionA.Id, email);
        var judgeB = NewJudge(competitionB.Id, email);

        await using (var seed = NewContext())
        {
            seed.AddRange(competitionA, competitionB, judgeA, judgeB);
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var resolver = new JudgeResolver(db);

        var resolved = await resolver.ResolveAndBackfillAsync("kc-sub-1", email, "Real Name");
        await db.SaveChangesAsync();

        Assert.Equal(2, resolved.Count);
        Assert.All(resolved, judge => Assert.Equal("kc-sub-1", judge.KeycloakUserId));
        Assert.All(resolved, judge => Assert.Equal("Real Name", judge.DisplayName));

        await using var verify = NewContext();
        var stored = await verify.Judges.AsNoTracking().Where(judge => judge.Email == email).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, judge => Assert.Equal("kc-sub-1", judge.KeycloakUserId));
        Assert.All(stored, judge => Assert.Equal("Real Name", judge.DisplayName));
    }

    [Fact]
    public async Task Replay_is_idempotent_and_leaves_an_already_backfilled_row_untouched()
    {
        var email = $"judge-{Guid.NewGuid():N}@example.test";
        var competition = NewCompetition();
        var judge = NewJudge(competition.Id, email);

        await using (var seed = NewContext())
        {
            seed.AddRange(competition, judge);
            await seed.SaveChangesAsync();
        }

        await using (var first = NewContext())
        {
            await new JudgeResolver(first).ResolveAndBackfillAsync("kc-sub-2", email, "First Name");
            await first.SaveChangesAsync();
        }

        // Replay with a different sub/name — an already-backfilled row must not change.
        await using var db = NewContext();
        var resolved = await new JudgeResolver(db).ResolveAndBackfillAsync("kc-sub-other", email, "Other Name");
        await db.SaveChangesAsync();

        var stored = Assert.Single(resolved);
        Assert.Equal("kc-sub-2", stored.KeycloakUserId);
        Assert.Equal("First Name", stored.DisplayName);
    }

    [Fact]
    public async Task Unmatched_email_returns_an_empty_list()
    {
        await using var db = NewContext();
        var resolver = new JudgeResolver(db);

        var resolved = await resolver.ResolveAndBackfillAsync(
            "kc-sub-3", $"nobody-{Guid.NewGuid():N}@example.test", "Someone");

        Assert.Empty(resolved);
    }

    [Fact]
    public async Task A_name_less_call_backfills_the_keycloak_user_id_but_leaves_display_name_untouched()
    {
        var email = $"judge-{Guid.NewGuid():N}@example.test";
        var competition = NewCompetition();
        var judge = NewJudge(competition.Id, email, displayName: "Default Local Part");

        await using (var seed = NewContext())
        {
            seed.AddRange(competition, judge);
            await seed.SaveChangesAsync();
        }

        await using var db = NewContext();
        var resolved = await new JudgeResolver(db).ResolveAndBackfillAsync("kc-sub-4", email, name: null);
        await db.SaveChangesAsync();

        var stored = Assert.Single(resolved);
        Assert.Equal("kc-sub-4", stored.KeycloakUserId);
        Assert.Equal("Default Local Part", stored.DisplayName);
    }

    private static Competition NewCompetition() => new()
    {
        Name = $"Judge resolver test {Guid.NewGuid():N}",
        Venue = "Test venue",
        StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2026, 9, 2),
        CreatedByUserId = $"kc-organizer-{Guid.NewGuid():N}",
    };

    private static Judge NewJudge(Guid competitionId, string email, string displayName = "Default Name") => new()
    {
        CompetitionId = competitionId,
        Email = email,
        DisplayName = displayName,
    };
}
