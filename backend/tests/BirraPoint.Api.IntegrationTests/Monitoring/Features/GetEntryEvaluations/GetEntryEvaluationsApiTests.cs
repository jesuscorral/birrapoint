using System.Net;
using System.Text.Json;
using BirraPoint.Api.Features.Evaluations.CommonComponents;
using BirraPoint.Api.Features.Evaluations.CommonComponents.Dtos;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Monitoring.Features.GetEntryEvaluations;

/// <summary>
/// T069: HTTP-level contract tests for GET /competitions/{id}/entries/{entryId}/evaluations
/// (contracts/rest-api.md §Monitoring &amp; audit, FR-042) — organizer-only audit drill-down.
/// </summary>
public sealed class GetEntryEvaluationsApiTests(ApiFactory factory) : MonitoringApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> GetEntryEvaluationsAsync(HttpClient client, Guid competitionId, Guid entryId) =>
        client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/entries/{entryId}/evaluations"));

    private static Task<HttpResponseMessage> CloseAsync(HttpClient client, Guid tableId) =>
        client.PostAsync(Urls.Rel($"/api/v1/me/tables/{tableId}/close"), content: null);

    [Fact]
    public async Task GetEntryEvaluationsReturnsJudgeNamesScoresCommentsTotalStatusAndNullMeanWhileOpen()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedTableAsync(competitionId, "Table A");

        var judge1Sub = $"judge-{Guid.NewGuid():N}";
        var judge1Id = await SeedJudgeAsync(competitionId, $"{judge1Sub}@brew.example", judge1Sub);
        await SeedTableJudgeAsync(tableId, judge1Id);
        var judge2Sub = $"judge-{Guid.NewGuid():N}";
        var judge2Id = await SeedJudgeAsync(competitionId, $"{judge2Sub}@brew.example", judge2Sub);
        await SeedTableJudgeAsync(tableId, judge2Id);

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Secret Beer");
        await SeedTableSampleAsync(tableId, entryId);

        await SeedEvaluationAsync(tableId, judge1Id, entryId, aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8); // total 39
        await SeedEvaluationAsync(tableId, judge2Id, entryId, aroma: 12, appearance: 3, flavor: 20, mouthfeel: 5, overall: 10); // total 50

        var response = await GetEntryEvaluationsAsync(organizer, competitionId, entryId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = document.RootElement;

        var evaluations = root.GetProperty("evaluations").EnumerateArray().ToList();
        Assert.Equal(2, evaluations.Count);

        var judge1Item = evaluations.Single(e => e.GetProperty("judgeDisplayName").GetString() == judge1Sub);
        Assert.Equal(10, judge1Item.GetProperty("scores").GetProperty("aroma").GetInt32());
        Assert.Equal(2, judge1Item.GetProperty("scores").GetProperty("appearance").GetInt32());
        Assert.Equal(15, judge1Item.GetProperty("scores").GetProperty("flavor").GetInt32());
        Assert.Equal(4, judge1Item.GetProperty("scores").GetProperty("mouthfeel").GetInt32());
        Assert.Equal(8, judge1Item.GetProperty("scores").GetProperty("overall").GetInt32());
        Assert.Equal(LongComment, judge1Item.GetProperty("comments").GetProperty("aroma").GetString());
        Assert.Equal(39, judge1Item.GetProperty("total").GetInt32());
        Assert.Equal("Confirmed", judge1Item.GetProperty("status").GetString());

        var judge2Item = evaluations.Single(e => e.GetProperty("judgeDisplayName").GetString() == judge2Sub);
        Assert.Equal(50, judge2Item.GetProperty("total").GetInt32());

        Assert.Equal(JsonValueKind.Null, root.GetProperty("consolidatedMean").ValueKind);
    }

    [Fact]
    public async Task GetEntryEvaluationsRoundTripsDescriptorsAndFeedbackWhenPresent()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedTableAsync(competitionId, "Table A");

        var judge1Sub = $"judge-{Guid.NewGuid():N}";
        var judge1Id = await SeedJudgeAsync(competitionId, $"{judge1Sub}@brew.example", judge1Sub);
        await SeedTableJudgeAsync(tableId, judge1Id);
        var judge2Sub = $"judge-{Guid.NewGuid():N}";
        var judge2Id = await SeedJudgeAsync(competitionId, $"{judge2Sub}@brew.example", judge2Sub);
        await SeedTableJudgeAsync(tableId, judge2Id);

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Secret Beer");
        await SeedTableSampleAsync(tableId, entryId);

        var descriptors = new EvaluationDescriptorsDto(
            new AppearanceDescriptorsDto(
                Color: "Golden", ColorOther: null, ColorInappropriate: false,
                Clarity: "Clear", ClarityInappropriate: false,
                Foam: "White", FoamOther: null, FoamInappropriate: false,
                Retention: 60, RetentionInappropriate: false,
                Texture: "Silky", Notes: null),
            new AromaDescriptorsDto(
                Malt: 2, MaltInappropriate: false, Hops: 3, HopsInappropriate: false,
                Fermentation: 1, FermentationInappropriate: false),
            null, null, null,
            ["Diacetyl", "Oxidized"]);
        await SeedEvaluationAsync(
            tableId, judge1Id, entryId,
            descriptorsJson: EvaluationDescriptorsSerializer.Serialize(descriptors),
            feedback: "Great balance, try dry-hopping a bit lighter next time.");
        // No descriptors/feedback for judge 2 — the null case must round-trip as null too, not an error.
        await SeedEvaluationAsync(tableId, judge2Id, entryId);

        var response = await GetEntryEvaluationsAsync(organizer, competitionId, entryId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var evaluations = document.RootElement.GetProperty("evaluations").EnumerateArray().ToList();

        var judge1Item = evaluations.Single(e => e.GetProperty("judgeDisplayName").GetString() == judge1Sub);
        Assert.Equal("Great balance, try dry-hopping a bit lighter next time.", judge1Item.GetProperty("feedback").GetString());
        var judge1Descriptors = judge1Item.GetProperty("descriptors");
        Assert.Equal("Golden", judge1Descriptors.GetProperty("appearance").GetProperty("color").GetString());
        Assert.Equal(60, judge1Descriptors.GetProperty("appearance").GetProperty("retention").GetInt32());
        Assert.Equal(2, judge1Descriptors.GetProperty("aroma").GetProperty("malt").GetInt32());
        var offFlavors = judge1Descriptors.GetProperty("offFlavors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Diacetyl", offFlavors);
        Assert.Contains("Oxidized", offFlavors);

        var judge2Item = evaluations.Single(e => e.GetProperty("judgeDisplayName").GetString() == judge2Sub);
        Assert.Equal(JsonValueKind.Null, judge2Item.GetProperty("feedback").ValueKind);
        Assert.Equal(JsonValueKind.Null, judge2Item.GetProperty("descriptors").ValueKind);
    }

    [Fact]
    public async Task GetEntryEvaluationsConsolidatedMeanIsPopulatedAfterTheTableCloses()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedTableAsync(competitionId, "Table A");

        var judge1Sub = $"judge-{Guid.NewGuid():N}";
        var judge1Id = await SeedJudgeAsync(competitionId, $"{judge1Sub}@brew.example", judge1Sub);
        await SeedTableJudgeAsync(tableId, judge1Id);
        var judge2Sub = $"judge-{Guid.NewGuid():N}";
        var judge2Id = await SeedJudgeAsync(competitionId, $"{judge2Sub}@brew.example", judge2Sub);
        await SeedTableJudgeAsync(tableId, judge2Id);

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Secret Beer");
        await SeedTableSampleAsync(tableId, entryId);

        await SeedEvaluationAsync(tableId, judge1Id, entryId, aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8); // total 39
        await SeedEvaluationAsync(tableId, judge2Id, entryId, aroma: 12, appearance: 3, flavor: 20, mouthfeel: 5, overall: 10); // total 50
        // mean of (39, 50) = 44.5

        using var judgeClient = JudgeClient(judge1Sub);
        var closeResponse = await CloseAsync(judgeClient, tableId);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        var response = await GetEntryEvaluationsAsync(organizer, competitionId, entryId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(44.5m, document.RootElement.GetProperty("consolidatedMean").GetDecimal());
    }

    [Fact]
    public async Task GetEntryEvaluationsForANonOwningOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var tableId = await SeedTableAsync(competitionId, "Table A");

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Secret Beer");
        await SeedTableSampleAsync(tableId, entryId);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetEntryEvaluationsAsync(other, competitionId, entryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEntryEvaluationsForAnEntryIdNotBelongingToTheCompetitionReturns404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var otherCompetitionId = await CreateCompetitionAsync(organizer, "Other");
        var otherParticipantId = await SeedParticipantAsync(otherCompetitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var otherEntryId = await SeedBeerEntryAsync(otherCompetitionId, otherParticipantId, "Other Beer");

        var response = await GetEntryEvaluationsAsync(organizer, competitionId, otherEntryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEntryEvaluationsForAnEntryNotYetAssignedToATableReturns404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Unassigned Beer");

        var response = await GetEntryEvaluationsAsync(organizer, competitionId, entryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
