using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Evaluations;

/// <summary>Shared clients, seeding and request helpers for the Evaluations slice contract tests
/// (contracts/rest-api.md §Judge workspace and §Monitoring &amp; audit). Only helpers that are
/// identical across the test classes live here; fixtures whose shape differs per suite stay private
/// in their class.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "Base class of public xUnit test classes; C# requires a base class to be at least as accessible as its derived classes.")]
public abstract class EvaluationsApiTestBase(ApiFactory factory)
{
    private protected const string StyleCodeApa = "21A";
    private protected const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

    private protected ApiFactory Factory => factory;

    private protected HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    /// <summary>Same email convention as TastingOrderApiTestBase — GetJudgeRecordsAsync resolves Judge rows
    /// by email, not `sub`.</summary>
    private protected HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, $"{sub}@brew.example", "JUDGE"));
        return client;
    }

    private protected static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Eval")
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
        Guid competitionId, Guid participantId, string beerName, string styleCode = StyleCodeApa)
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
        };
        db.BeerEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

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

    private protected async Task SeedTableJudgeAsync(Guid tableId, Guid judgeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableJudges.Add(new TableJudge { TastingTableId = tableId, JudgeId = judgeId });
        await db.SaveChangesAsync();
    }

    private protected async Task SeedTableSampleAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableSamples.Add(new TableSample { TastingTableId = tableId, BeerEntryId = beerEntryId });
        await db.SaveChangesAsync();
    }

    /// <summary>Directly assigns SequenceOrder (1..M, per <paramref name="orderedEntryIds"/>) and
    /// sets OrderFixedByJudgeId/OrderFixedAt — mirrors what POST .../order (FixOrder, T050) would
    /// do, without needing the extra HTTP round-trip for tests that only care about downstream
    /// evaluation preconditions.</summary>
    private protected async Task FixOrderDirectlyAsync(Guid tableId, Guid judgeId, List<Guid> orderedEntryIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var table = await db.TastingTables.SingleAsync(t => t.Id == tableId);
        table.OrderFixedByJudgeId = judgeId;
        table.OrderFixedAt = DateTimeOffset.UtcNow;

        var samples = await db.TableSamples.Where(ts => ts.TastingTableId == tableId).ToListAsync();
        for (var i = 0; i < orderedEntryIds.Count; i++)
        {
            samples.Single(s => s.BeerEntryId == orderedEntryIds[i]).SequenceOrder = i + 1;
        }

        await db.SaveChangesAsync();
    }

    private protected static object ValidScores() => new { aroma = 10, appearance = 2, flavor = 15, mouthfeel = 4, overall = 8 };

    private protected static object ValidComments() => new
    {
        aroma = LongComment,
        appearance = LongComment,
        flavor = LongComment,
        mouthfeel = LongComment,
        overall = LongComment,
    };

    private protected static Task<HttpResponseMessage> CloseAsync(HttpClient client, Guid tableId) =>
        client.PostAsync(Urls.Rel($"/api/v1/me/tables/{tableId}/close"), content: null);

    // ---- Discrepancy / adjustment helpers shared by DiscrepancyApiTests and AdjustEvaluationApiTests ----

    private protected async Task<int> CountOpenAlertsAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DiscrepancyAlerts.CountAsync(
            a => a.TastingTableId == tableId && a.BeerEntryId == beerEntryId && a.Status == DiscrepancyStatus.Open);
    }

    private protected static object Scores(int aroma, int appearance, int flavor, int mouthfeel, int overall) =>
        new { aroma, appearance, flavor, mouthfeel, overall };

    private protected static Task<HttpResponseMessage> AdjustAsync(
        HttpClient client, Guid tableId, Guid evaluationId, object scores, object? comments = null) =>
        client.PutAsJsonAsync(
            $"/api/v1/me/tables/{tableId}/evaluations/{evaluationId}",
            new { scores, comments = comments ?? ValidComments() });

    private protected static Task<HttpResponseMessage> GetDiscrepanciesAsync(HttpClient client, Guid tableId) =>
        client.GetAsync(Urls.Rel($"/api/v1/me/tables/{tableId}/discrepancies"));
}
