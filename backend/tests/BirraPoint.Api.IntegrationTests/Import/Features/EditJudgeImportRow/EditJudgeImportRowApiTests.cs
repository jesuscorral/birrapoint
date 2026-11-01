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

namespace BirraPoint.Api.IntegrationTests.Import.Features.EditJudgeImportRow;

/// <summary>
/// HTTP-level contract tests for PUT /competitions/{id}/imports/judges/{importId}/rows/{rowNumber} (contracts/rest-api.md §Judge Roster Import) — full-row edit, status recomputation and role gating.
/// </summary>
public sealed class EditJudgeImportRowApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task EditRowWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(email: null));

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await EditJudgeRowAsync(judge, competitionId, importId, rowNumber: 1, FullJudgeEditBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EditRowResolvingAMissingEmailLetsConsolidationIncludeIt()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(email: null));
        var fixedEmail = $"fixed-{Guid.NewGuid():N}@brew.example";

        var edit = await EditJudgeRowAsync(organizer, competitionId, importId, rowNumber: 1, FullJudgeEditBody(email: fixedEmail));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var editDocument = JsonDocument.Parse(await edit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Valid", editDocument.RootElement.GetProperty("status").GetString());

        var consolidate = await ConsolidateJudgeAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);
        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var created = document.RootElement.GetProperty("created").EnumerateArray().ToList();
        Assert.Single(created);
        Assert.Equal(fixedEmail, created[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task EditRowLeavingTheEmailUnresolvedRecomputesStatusInvalid()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(email: null));

        var edit = await EditJudgeRowAsync(organizer, competitionId, importId, rowNumber: 1, FullJudgeEditBody(email: null));

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var document = JsonDocument.Parse(await edit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Invalid", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task EditRowOnAnExcludedRowReturns400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow());
        await ExcludeJudgeRowAsync(organizer, competitionId, importId, rowNumber: 1);

        var response = await EditJudgeRowAsync(organizer, competitionId, importId, rowNumber: 1, FullJudgeEditBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }
}
