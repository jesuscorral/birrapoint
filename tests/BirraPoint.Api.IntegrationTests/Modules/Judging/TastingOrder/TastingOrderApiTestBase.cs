using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.TastingOrder;

/// <summary>Shared clients, seeding and request helpers for the judge-facing tasting-order contract tests (contracts/rest-api.md §Judge workspace). Only helpers used by more than one test class live here; the rest stay private in their class.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "Base class of public xUnit test classes; C# requires a base class to be at least as accessible as its derived classes.")]
public abstract class TastingOrderApiTestBase(ApiFactory factory)
{
    private protected ApiFactory Factory => factory;

    private protected const string StyleCodeApa = "21A";

    private protected HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    /// <summary>
    /// JudgeResolver.ResolveAndBackfillAsync (via IJudgeDirectory) resolves Judge
    /// rows by email, not by `sub` — KeycloakUserId is only ever used to backfill, never as a
    /// lookup key (see Common/Auth/JudgeResolver.cs). So the test JWT must carry the same email
    /// the Judge row was seeded with; every SeedJudgeAsync call below uses the deterministic
    /// `{sub}@brew.example` convention this derives, keeping seed and client in lockstep.
    /// </summary>
    private protected HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, $"{sub}@brew.example", "JUDGE"));
        return client;
    }

    private protected static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Order")
    {
        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = $"{namePrefix} {Guid.NewGuid():N}",
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    private protected static Task<HttpResponseMessage> TransitionStateAsync(HttpClient client, Guid competitionId, string target) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target });

    private protected static string NewBlindCode() => $"B{Guid.NewGuid():N}"[..8];

    private protected async Task<Guid> SeedParticipantAsync(Guid competitionId, string name, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var participant = new Participant { CompetitionId = competitionId, Name = name, Email = email };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();
        return participant.Id;
    }

    private protected async Task<Guid> SeedBeerEntryAsync(
        Guid competitionId, Guid participantId, string beerName, string styleCode = StyleCodeApa,
        string? entryInstructions = null, decimal abvPercent = 5.5m)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = new BeerEntry
        {
            CompetitionId = competitionId,
            ParticipantId = participantId,
            BeerName = beerName,
            StyleCode = styleCode,
            BlindCode = NewBlindCode(),
            EntryInstructions = entryInstructions,
            AbvPercent = abvPercent,
        };
        db.BeerEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    /// <summary>Seeds a Judge with a pre-set KeycloakUserId so a test JWT minted with the same
    /// `sub` authenticates as this judge without a real Keycloak login round-trip — KeycloakUserId
    /// is otherwise only backfilled by a real login through JudgeResolver (same constraint
    /// documented by JudgesApiTestBase.SeedJudgeAsync).</summary>
    private protected async Task<Guid> SeedJudgeAsync(Guid competitionId, string email, string keycloakUserId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = new Judge
        {
            CompetitionId = competitionId,
            Email = email,
            DisplayName = email.Split('@')[0],
            KeycloakUserId = keycloakUserId,
        };
        db.Judges.Add(judge);
        await db.SaveChangesAsync();
        return judge.Id;
    }

    private protected async Task<Guid> SeedTableAsync(Guid competitionId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = new TastingTable { CompetitionId = competitionId, Name = name };
        db.TastingTables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    private protected async Task SeedTableJudgeAsync(Guid tableId, Guid judgeId, DateTimeOffset? removedAt = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableJudges.Add(new TableJudge { TastingTableId = tableId, JudgeId = judgeId, RemovedAt = removedAt });
        await db.SaveChangesAsync();
    }

    private protected async Task SeedTableSampleAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableSamples.Add(new TableSample { TastingTableId = tableId, BeerEntryId = beerEntryId });
        await db.SaveChangesAsync();
    }

    private protected static Task<HttpResponseMessage> GetMyTablesAsync(HttpClient client) =>
        client.GetAsync(Urls.Rel("/api/v1/me/tables"));

    /// <summary>Seeds a competition already Active, one table with <paramref name="sampleCount"/>
    /// samples, and one judge actively assigned to it — the common fixture for the
    /// GET .../samples / POST .../order tests below.</summary>
    private protected async Task<(Guid CompetitionId, Guid TableId, Guid JudgeId, string JudgeSub, List<Guid> EntryIds)>
        SeedActiveTableWithSamplesAsync(HttpClient organizer, int sampleCount = 2)
    {
        var competitionId = await CreateCompetitionAsync(organizer);
        await TransitionStateAsync(organizer, competitionId, "Active");

        var tableId = await SeedTableAsync(competitionId, $"Table {Guid.NewGuid():N}");

        var judgeSub = $"judge-{Guid.NewGuid():N}";
        var judgeEmail = $"{judgeSub}@brew.example";
        var judgeId = await SeedJudgeAsync(competitionId, judgeEmail, judgeSub);
        await SeedTableJudgeAsync(tableId, judgeId);

        var entryIds = new List<Guid>();
        for (var i = 0; i < sampleCount; i++)
        {
            var participantId = await SeedParticipantAsync(
                competitionId, $"Brewer {i}", $"brewer-{Guid.NewGuid():N}@brew.example");
            var entryId = await SeedBeerEntryAsync(competitionId, participantId, $"Secret Beer {i}");
            await SeedTableSampleAsync(tableId, entryId);
            entryIds.Add(entryId);
        }

        return (competitionId, tableId, judgeId, judgeSub, entryIds);
    }
}
