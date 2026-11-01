using System.Net;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Import.Features.GetBeerImport;

/// <summary>
/// HTTP-level contract tests for GET /competitions/{id}/imports/beers/{importId} (current row states).
/// </summary>
public sealed class GetBeerImportApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> GetBeerImportAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports/beers/{importId}"));

    [Fact]
    public async Task GetImportReturns404ForACompetitionOwnedByADifferentOrganizer()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        await SetStandardCategoryAsync(owner, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(owner, competitionId, BeerRow());

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetBeerImportAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetImportWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow());

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetBeerImportAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOnThePreUnificationImportsRouteReturns404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow());

        var response = await organizer.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports/{importId}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
