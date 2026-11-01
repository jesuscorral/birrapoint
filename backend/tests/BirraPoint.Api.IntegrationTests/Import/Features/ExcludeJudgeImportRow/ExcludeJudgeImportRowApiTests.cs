using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Import.Features.ExcludeJudgeImportRow;

/// <summary>
/// HTTP-level contract tests for POST /competitions/{id}/imports/judges/{importId}/rows/{rowNumber}/exclude (contracts/rest-api.md §Judge Roster Import) — exclusion and its effect on consolidation gating.
/// </summary>
public sealed class ExcludeJudgeImportRowApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ExcludeRowWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(email: null));

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ExcludeJudgeRowAsync(judge, competitionId, importId, rowNumber: 1);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExcludeRowSetsStatusExcludedAndItIsNoLongerAConsolidationBlocker()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(email: null));

        var exclude = await ExcludeJudgeRowAsync(organizer, competitionId, importId, rowNumber: 1);
        Assert.Equal(HttpStatusCode.OK, exclude.StatusCode);
        using var excludeDocument = JsonDocument.Parse(await exclude.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Excluded", excludeDocument.RootElement.GetProperty("status").GetString());

        var consolidate = await ConsolidateJudgeAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);
        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, document.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(1, document.RootElement.GetProperty("excluded").GetInt32());
    }
}
