using System.Net;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Import.Features.GetImport;

/// <summary>
/// T032: HTTP-level contract tests for GET /competitions/{id}/imports/{importId} (current row states).
/// </summary>
public sealed class GetImportApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> GetImportAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports/{importId}"));

    [Fact]
    public async Task GetImportReturns404ForACompetitionOwnedByADifferentOrganizer()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        await SetStandardCategoryAsync(owner, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(owner, competitionId, Row());

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetImportAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetImportWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetImportAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
