using System.Net;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Import.Features.ExcludeBeerImportRow;

/// <summary>
/// HTTP-level contract tests for POST /competitions/{id}/imports/beers/{importId}/rows/{rowNumber}/exclude.
/// </summary>
public sealed class ExcludeBeerImportRowApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ExcludeRowWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow(style: $"{UnknownStyleCode}. X"));

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ExcludeBeerRowAsync(judge, competitionId, importId, rowNumber: 1);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExcludeRowSetsStatusExcludedAndItIsNoLongerAConsolidationBlocker()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow(style: $"{UnknownStyleCode}. X"));

        var exclude = await ExcludeBeerRowAsync(organizer, competitionId, importId, rowNumber: 1);
        Assert.Equal(HttpStatusCode.OK, exclude.StatusCode);
        using var excludeDocument = JsonDocument.Parse(await exclude.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Excluded", excludeDocument.RootElement.GetProperty("status").GetString());

        var consolidate = await ConsolidateBeerAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);
        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, document.RootElement.GetProperty("imported").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("excluded").GetInt32());
    }
}
