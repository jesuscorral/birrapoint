using System.Net;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.TastingOrder.Features.GetTableSamples;

/// <summary>
/// HTTP-level contract tests for GET /me/tables/{tableId}/samples — membership scoping and the
/// BR-01 anonymity boundary on the serialized wire payload (not just the DTO's declared shape).
/// </summary>
public sealed class GetTableSamplesApiTests(ApiFactory factory) : TastingOrderApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> GetTableSamplesAsync(HttpClient client, Guid tableId) =>
        client.GetAsync(Urls.Rel($"/api/v1/me/tables/{tableId}/samples"));

    [Fact]
    public async Task GetTableSamplesForATableTheJudgeIsNotAssignedToReturns404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, _, _) = await SeedActiveTableWithSamplesAsync(organizer);

        using var otherJudge = JudgeClient($"outsider-{Guid.NewGuid():N}");
        var response = await GetTableSamplesAsync(otherJudge, tableId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>BR-01/FR-019 structural test — asserts the serialized wire payload, not just the
    /// declared DTO shape, never carries entrant fields.</summary>
    [Fact]
    public async Task GetTableSamplesPayloadNeverContainsEntrantFields()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, judgeSub, _) = await SeedActiveTableWithSamplesAsync(organizer, sampleCount: 3);

        using var judge = JudgeClient(judgeSub);
        var response = await GetTableSamplesAsync(judge, tableId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        foreach (var forbidden in new[] { "beername", "participant", "brewery", "origin", "collaborator" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>EntryInstructions is the one deliberate exception to BR-01/FR-019 (alongside
    /// BlindCode/StyleCode) — this asserts it round-trips to the judge, unlike every entrant field
    /// asserted absent above.</summary>
    [Fact]
    public async Task GetTableSamplesIncludesEntryInstructionsSinceItIsJudgeFacingByDesign()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await TransitionStateAsync(organizer, competitionId, "Active");
        var tableId = await SeedTableAsync(competitionId, $"Table {Guid.NewGuid():N}");

        var judgeSub = $"judge-{Guid.NewGuid():N}";
        var judgeId = await SeedJudgeAsync(competitionId, $"{judgeSub}@brew.example", judgeSub);
        await SeedTableJudgeAsync(tableId, judgeId);

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(
            competitionId, participantId, "Secret Beer", entryInstructions: "Serve at cellar temperature.");
        await SeedTableSampleAsync(tableId, entryId);

        using var judge = JudgeClient(judgeSub);
        var response = await GetTableSamplesAsync(judge, tableId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var sample = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("Serve at cellar temperature.", sample.GetProperty("entryInstructions").GetString());
    }

    /// <summary>AbvPercent is the beer's own attribute, not an entrant field — safe alongside
    /// BlindCode/StyleCode (same value the organizer sees via TableSampleDto).</summary>
    [Fact]
    public async Task GetTableSamplesIncludesTheRealAbvPercentOfEachEntry()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await TransitionStateAsync(organizer, competitionId, "Active");
        var tableId = await SeedTableAsync(competitionId, $"Table {Guid.NewGuid():N}");

        var judgeSub = $"judge-{Guid.NewGuid():N}";
        var judgeId = await SeedJudgeAsync(competitionId, $"{judgeSub}@brew.example", judgeSub);
        await SeedTableJudgeAsync(tableId, judgeId);

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Secret Beer", abvPercent: 6.8m);
        await SeedTableSampleAsync(tableId, entryId);

        using var judge = JudgeClient(judgeSub);
        var response = await GetTableSamplesAsync(judge, tableId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var sample = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(6.8m, sample.GetProperty("abvPercent").GetDecimal());
    }
}
