using System.Net;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.TastingOrder.Features.GetTableJudges;

/// <summary>
/// HTTP-level contract tests for GET /me/tables/{tableId}/judges.
/// </summary>
public sealed class GetTableJudgesApiTests(ApiFactory factory) : TastingOrderApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> GetTableJudgesAsync(HttpClient client, Guid tableId) =>
        client.GetAsync(Urls.Rel($"/api/v1/me/tables/{tableId}/judges"));

    [Fact]
    public async Task GetTableJudgesForATableTheJudgeIsNotAssignedToReturns404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, _, _) = await SeedActiveTableWithSamplesAsync(organizer);

        using var otherJudge = JudgeClient($"outsider-{Guid.NewGuid():N}");
        var response = await GetTableJudgesAsync(otherJudge, tableId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetTableJudgesReturnsOtherActiveJudgesExcludingTheCallerAndAnyoneRemoved()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (competitionId, tableId, _, callerSub, _) = await SeedActiveTableWithSamplesAsync(organizer);

        var otherSub = $"judge-{Guid.NewGuid():N}";
        var otherId = await SeedJudgeAsync(competitionId, $"{otherSub}@brew.example", otherSub);
        await SeedTableJudgeAsync(tableId, otherId);

        var removedSub = $"judge-{Guid.NewGuid():N}";
        var removedId = await SeedJudgeAsync(competitionId, $"{removedSub}@brew.example", removedSub);
        await SeedTableJudgeAsync(tableId, removedId, removedAt: DateTimeOffset.UtcNow);

        using var caller = JudgeClient(callerSub);
        var response = await GetTableJudgesAsync(caller, tableId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var members = document.RootElement.EnumerateArray().ToList();
        var member = Assert.Single(members);
        Assert.Equal($"{otherSub}@brew.example".Split('@')[0], member.GetProperty("displayName").GetString());
    }

    /// <summary>Same BR-01/FR-019 structural check as GetTableSamples — this endpoint never
    /// carries entrant data either, only judge display info.</summary>
    [Fact]
    public async Task GetTableJudgesPayloadNeverContainsEntrantFields()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (competitionId, tableId, _, callerSub, _) = await SeedActiveTableWithSamplesAsync(organizer);
        var otherSub = $"judge-{Guid.NewGuid():N}";
        var otherId = await SeedJudgeAsync(competitionId, $"{otherSub}@brew.example", otherSub);
        await SeedTableJudgeAsync(tableId, otherId);

        using var caller = JudgeClient(callerSub);
        var response = await GetTableJudgesAsync(caller, tableId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        foreach (var forbidden in new[] { "beername", "participant", "brewery", "origin", "collaborator", "email" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }
}
