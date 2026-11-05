using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Import.Features.GetJudgeImport;

/// <summary>
/// HTTP-level contract tests for GET /competitions/{id}/imports/judges/{importId} (contracts/rest-api.md §Judge Roster Import) — ownership scoping and role gating; also proves the pre-unification /judge-imports route is gone.
/// </summary>
public sealed class GetJudgeImportApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetJudgeImportReturns404ForACompetitionOwnedByADifferentOrganizer()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var (importId, _) = await UploadJudgeRowsAsync(owner, competitionId, JudgeRow());

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetJudgeImportAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetJudgeImportWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow());

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetJudgeImportAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOnThePreUnificationJudgeImportsRouteReturns404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow());

        var response = await organizer.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/judge-imports/{importId}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
