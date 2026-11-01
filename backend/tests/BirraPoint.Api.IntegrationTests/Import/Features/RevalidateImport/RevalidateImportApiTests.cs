using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Import.Features.RevalidateImport;

/// <summary>
/// HTTP-level contract tests for POST /competitions/{id}/imports/{importId}/revalidate (FR-054).
/// </summary>
public sealed class RevalidateImportApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> RevalidateAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports/{importId}/revalidate"), null);

    [Fact]
    public async Task RevalidateWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await RevalidateAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevalidateForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        await SetStandardCategoryAsync(owner, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(owner, competitionId, Row());

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await RevalidateAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevalidateWithAnUnknownImportIdReturns404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var response = await RevalidateAsync(organizer, competitionId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevalidateResolvesACategoryStyleMismatchRowOnceTheMissingStyleAssignmentIsAdded()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var firstCategoryId = await SetStandardCategoryAsync(organizer, competitionId, styleCodes: [StyleCodeHazyIpa]);
        var (importId, rows) = await UploadAcceRowsAsync(organizer, competitionId, Row(style: "20C. Imperial Stout"));
        Assert.Equal("CategoryStyleMismatch", rows[0].GetProperty("status").GetString());

        // Full-replace PUT with the same category name but Stout now assigned too — this always
        // mints a brand-new CompetitionCategory id, so this also exercises the "stale id, re-match
        // by name" path, not just an allow-list re-check against an unchanged id.
        var secondCategoryId = await SetStandardCategoryAsync(
            organizer, competitionId, styleCodes: [StyleCodeHazyIpa, StyleCodeStout]);
        Assert.NotEqual(firstCategoryId, secondCategoryId);

        var response = await RevalidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var row = document.RootElement.GetProperty("rows")[0];
        Assert.Equal("Valid", row.GetProperty("status").GetString());
        var data = row.GetProperty("data");
        Assert.Equal(secondCategoryId, data.GetProperty("competitionCategoryId").GetGuid());
        Assert.Equal(StyleCodeStout, data.GetProperty("resolvedStyleCode").GetString());
    }

    [Fact]
    public async Task RevalidateTurnsAPreviouslyValidRowIntoCategoryMismatchWhenItsCategoryNoLongerExists()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, rows) = await UploadAcceRowsAsync(organizer, competitionId, Row());
        Assert.Equal("Valid", rows[0].GetProperty("status").GetString());

        // Full-replace PUT under a different category name — "Estilos clásicos" no longer exists.
        var putResponse = await organizer.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Otra categoria", displayOrder = 0, styleCodes = new[] { StyleCodeHazyIpa } } },
        }, cancellationToken: TestContext.Current.CancellationToken);
        putResponse.EnsureSuccessStatusCode();

        var response = await RevalidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var row = document.RootElement.GetProperty("rows")[0];
        Assert.Equal("CategoryMismatch", row.GetProperty("status").GetString());
        var data = row.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("competitionCategoryId").ValueKind);
        // The previously-resolved style is preserved even though the category became unresolved.
        Assert.Equal(StyleCodeHazyIpa, data.GetProperty("resolvedStyleCode").GetString());
    }

    [Fact]
    public async Task RevalidateOnAnAlreadyConsolidatedBatchIsANoOp()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());
        var consolidate = await ConsolidateAsync(organizer, competitionId, importId);
        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);

        var response = await RevalidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var row = document.RootElement.GetProperty("rows")[0];
        Assert.Equal("Valid", row.GetProperty("status").GetString());
    }
}
