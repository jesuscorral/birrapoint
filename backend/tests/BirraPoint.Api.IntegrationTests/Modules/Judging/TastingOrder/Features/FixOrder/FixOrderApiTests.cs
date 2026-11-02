using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.TastingOrder.Features.FixOrder;

/// <summary>
/// HTTP-level contract tests for the one-shot POST /me/tables/{tableId}/order, incl. the race
/// (US6-4 / Clarification Q1).
/// </summary>
public sealed class FixOrderApiTests(ApiFactory factory) : TastingOrderApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> FixOrderAsync(HttpClient client, Guid tableId, IEnumerable<Guid> orderedBeerEntryIds) =>
        client.PostAsJsonAsync($"/api/v1/me/tables/{tableId}/order", new { orderedBeerEntryIds });

    [Fact]
    public async Task FixOrderHappyPathAssignsSequenceAndLocksTheTable()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, judgeSub, entryIds) = await SeedActiveTableWithSamplesAsync(organizer, sampleCount: 3);

        using var judge = JudgeClient(judgeSub);
        var response = await FixOrderAsync(judge, tableId, entryIds);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var samples = document.RootElement.EnumerateArray().ToList();
        Assert.Equal(entryIds.Count, samples.Count);
        Assert.All(samples, s => Assert.Equal(JsonValueKind.Number, s.GetProperty("sequenceOrder").ValueKind));

        var tablesResponse = await GetMyTablesAsync(judge);
        using var tablesDocument = JsonDocument.Parse(await tablesResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var table = tablesDocument.RootElement.EnumerateArray().Single(t => t.GetProperty("tableId").GetGuid() == tableId);
        Assert.True(table.GetProperty("orderFixed").GetBoolean());
    }

    [Fact]
    public async Task FixOrderWithANonPermutationBodyReturns400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, judgeSub, entryIds) = await SeedActiveTableWithSamplesAsync(organizer, sampleCount: 2);

        using var judge = JudgeClient(judgeSub);
        var response = await FixOrderAsync(judge, tableId, [entryIds[0]]); // missing one id

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>US6-4 / Clarification Q1: two judges race to fix the same table's order —
    /// exactly one wins, the other gets 409 order-already-fixed.</summary>
    [Fact]
    public async Task FixOrderRaceOnlyOneConcurrentRequestWins()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, judgeSub, entryIds) = await SeedActiveTableWithSamplesAsync(organizer, sampleCount: 3);

        using var judgeA = JudgeClient(judgeSub);
        using var judgeB = JudgeClient(judgeSub);

        var reversed = entryIds.AsEnumerable().Reverse().ToList();

        var responses = await Task.WhenAll(
            FixOrderAsync(judgeA, tableId, entryIds),
            FixOrderAsync(judgeB, tableId, reversed));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var conflictResponse = responses.First(r => r.StatusCode == HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await conflictResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("order-already-fixed", document.RootElement.GetProperty("type").GetString(), StringComparison.Ordinal);
    }
}
