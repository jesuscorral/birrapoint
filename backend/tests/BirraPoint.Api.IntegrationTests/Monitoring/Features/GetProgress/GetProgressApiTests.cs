using System.Net;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Monitoring.Features.GetProgress;

/// <summary>
/// HTTP-level contract tests for GET /competitions/{id}/progress (contracts/rest-api.md §Monitoring &amp;
/// audit, FR-038) — organizer-only dashboard init.
/// </summary>
public sealed class GetProgressApiTests(ApiFactory factory) : MonitoringApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> GetProgressAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/progress"));

    [Fact]
    public async Task GetProgressReturnsCompletedExpectedPercentAcrossTables()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        // Table A: 1 judge, 2 samples, only 1 evaluation submitted -> 1/2 = 50%.
        var tableAId = await SeedTableAsync(competitionId, "Table A");
        var judgeA1Sub = $"judge-{Guid.NewGuid():N}";
        var judgeA1Id = await SeedJudgeAsync(competitionId, $"{judgeA1Sub}@brew.example", judgeA1Sub);
        await SeedTableJudgeAsync(tableAId, judgeA1Id);

        var participantA1 = await SeedParticipantAsync(competitionId, "Brewer A1", $"a1-{Guid.NewGuid():N}@brew.example");
        var entryA1 = await SeedBeerEntryAsync(competitionId, participantA1, "Beer A1");
        await SeedTableSampleAsync(tableAId, entryA1);

        var participantA2 = await SeedParticipantAsync(competitionId, "Brewer A2", $"a2-{Guid.NewGuid():N}@brew.example");
        var entryA2 = await SeedBeerEntryAsync(competitionId, participantA2, "Beer A2");
        await SeedTableSampleAsync(tableAId, entryA2);

        await SeedEvaluationAsync(tableAId, judgeA1Id, entryA1);

        // Table B: 2 judges, 1 sample, both evaluations submitted -> 2/2 = 100%.
        var tableBId = await SeedTableAsync(competitionId, "Table B");
        var judgeB1Sub = $"judge-{Guid.NewGuid():N}";
        var judgeB1Id = await SeedJudgeAsync(competitionId, $"{judgeB1Sub}@brew.example", judgeB1Sub);
        await SeedTableJudgeAsync(tableBId, judgeB1Id);
        var judgeB2Sub = $"judge-{Guid.NewGuid():N}";
        var judgeB2Id = await SeedJudgeAsync(competitionId, $"{judgeB2Sub}@brew.example", judgeB2Sub);
        await SeedTableJudgeAsync(tableBId, judgeB2Id);

        var participantB1 = await SeedParticipantAsync(competitionId, "Brewer B1", $"b1-{Guid.NewGuid():N}@brew.example");
        var entryB1 = await SeedBeerEntryAsync(competitionId, participantB1, "Beer B1");
        await SeedTableSampleAsync(tableBId, entryB1);

        await SeedEvaluationAsync(tableBId, judgeB1Id, entryB1);
        await SeedEvaluationAsync(tableBId, judgeB2Id, entryB1);

        var response = await GetProgressAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var tables = document.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, tables.Count);

        var tableA = tables.Single(t => t.GetProperty("tableId").GetGuid() == tableAId);
        Assert.Equal("Table A", tableA.GetProperty("name").GetString());
        Assert.Equal("Open", tableA.GetProperty("state").GetString());
        Assert.Equal(1, tableA.GetProperty("completed").GetInt32());
        Assert.Equal(2, tableA.GetProperty("expected").GetInt32());
        Assert.Equal(50, tableA.GetProperty("percent").GetInt32());

        var tableB = tables.Single(t => t.GetProperty("tableId").GetGuid() == tableBId);
        Assert.Equal("Table B", tableB.GetProperty("name").GetString());
        Assert.Equal("Open", tableB.GetProperty("state").GetString());
        Assert.Equal(2, tableB.GetProperty("completed").GetInt32());
        Assert.Equal(2, tableB.GetProperty("expected").GetInt32());
        Assert.Equal(100, tableB.GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task GetProgressForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetProgressAsync(other, competitionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetProgressWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetProgressAsync(judge, competitionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
