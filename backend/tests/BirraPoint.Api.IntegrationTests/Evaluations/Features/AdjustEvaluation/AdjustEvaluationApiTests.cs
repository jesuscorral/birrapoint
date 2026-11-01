using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Evaluations.Features.AdjustEvaluation;

/// <summary>
/// T080: HTTP-level contract tests for PUT /me/tables/{tableId}/evaluations/{evaluationId}
/// (FR-031) — gated to involved judges only while the discrepancy alert is open (Clarification Q2,
/// `409 evaluation-locked` otherwise, `404` for someone else's evaluation).
/// </summary>
public sealed class AdjustEvaluationApiTests(ApiFactory factory) : EvaluationsApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, Guid tableId, Guid beerEntryId, object scores, object? comments = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/me/tables/{tableId}/evaluations")
        {
            Content = JsonContent.Create(new { beerEntryId, scores, comments = comments ?? ValidComments() }),
        };
        request.Headers.Add("X-Idempotency-Key", $"comp:{tableId}:judge:{beerEntryId}");
        return await client.SendAsync(request);
    }

    /// <summary>Seeds a competition, one table with a single sample, and <paramref name="judgeCount"/>
    /// actively-assigned judges — ready for submission (competition InEvaluation, order fixed by the
    /// first seeded judge). A single shared sample keeps the "next in sequence" gate trivially
    /// satisfied for every judge (FR-022 is not what this suite is about).</summary>
    private async Task<(Guid CompetitionId, Guid TableId, List<(Guid JudgeId, string JudgeSub)> Judges, Guid EntryId)>
        SeedReadyTableAsync(HttpClient organizer, int judgeCount)
    {
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedTableAsync(competitionId, $"Table {Guid.NewGuid():N}");

        var judges = new List<(Guid JudgeId, string JudgeSub)>();
        for (var j = 0; j < judgeCount; j++)
        {
            var judgeSub = $"judge-{Guid.NewGuid():N}";
            var judgeId = await SeedJudgeAsync(competitionId, $"{judgeSub}@brew.example", judgeSub);
            await SeedTableJudgeAsync(tableId, judgeId);
            judges.Add((judgeId, judgeSub));
        }

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Secret Beer");
        await SeedTableSampleAsync(tableId, entryId);

        await TransitionStateAsync(organizer, competitionId, "Active");
        await TransitionStateAsync(organizer, competitionId, "InEvaluation");
        await FixOrderDirectlyAsync(tableId, judges[0].JudgeId, [entryId]);

        return (competitionId, tableId, judges, entryId);
    }

    // ---- PUT adjustment: only an involved judge, only while the alert is open (Clarification Q2) --

    [Fact]
    public async Task AdjustmentByAnInvolvedJudgeThatConvergesEveryoneResolvesTheDiscrepancy()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);

        var first = await SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8)); // 40
        var evaluationIdA = (await first.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("evaluationId").GetGuid();

        var second = await SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(5, 1, 10, 2, 2)); // 20, diff 20 -> both involved
        Assert.Equal("PendingConsensus", (await second.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("status").GetString());

        var adjustment = await AdjustAsync(judgeA, fixture.TableId, evaluationIdA, Scores(6, 1, 12, 3, 3)); // 25, diff to 20 is 5

        Assert.Equal(HttpStatusCode.OK, adjustment.StatusCode);
        using var adjustedDocument = JsonDocument.Parse(await adjustment.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var adjustedRoot = adjustedDocument.RootElement;
        Assert.Equal("Confirmed", adjustedRoot.GetProperty("status").GetString());
        Assert.Equal(25, adjustedRoot.GetProperty("total").GetInt32());
        Assert.False(adjustedRoot.TryGetProperty("discrepancy", out var discrepancyAfter) && discrepancyAfter.ValueKind != JsonValueKind.Null);

        Assert.Equal(0, await CountOpenAlertsAsync(fixture.TableId, fixture.EntryId));

        var remainingDiscrepancies = await GetDiscrepanciesAsync(judgeB, fixture.TableId);
        using var remaining = JsonDocument.Parse(await remainingDiscrepancies.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(remaining.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task AdjustmentOfAnEvaluationWithNoOpenDiscrepancyReturns409EvaluationLocked()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 1);
        using var judge = JudgeClient(fixture.Judges[0].JudgeSub);

        var submit = await SubmitAsync(judge, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8));
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("evaluationId").GetGuid();

        var response = await AdjustAsync(judge, fixture.TableId, evaluationId, Scores(9, 2, 15, 4, 8));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("evaluation-locked", document.RootElement.GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdjustmentOfSomeoneElsesEvaluationReturns404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);

        var first = await SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8)); // 40
        var evaluationIdA = (await first.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("evaluationId").GetGuid();
        await SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(5, 1, 10, 2, 2)); // 20, opens the alert

        var response = await AdjustAsync(judgeB, fixture.TableId, evaluationIdA, Scores(9, 2, 15, 4, 8));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
