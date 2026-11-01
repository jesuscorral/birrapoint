using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Import.Features.EditImportRow;

/// <summary>
/// T032: HTTP-level contract tests for the full-row-edit PUT /competitions/{id}/imports/{importId}/rows/{rowNumber}.
/// </summary>
public sealed class EditImportRowApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> EditRowAsync(
        HttpClient client, Guid competitionId, Guid importId, int rowNumber, object body) =>
        client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/imports/{importId}/rows/{rowNumber}", body);

    /// <summary>Full editable-row body matching <c>EditImportRowRequest</c>'s camelCase wire shape.</summary>
    private static object FullEditBody(
        string participantName = "José Deza Prieto",
        string participantEmail = "dezaprieto@gmail.com",
        string? acceMemberNumber = "1423",
        string? dateOfBirth = null,
        string? phone = "699989612",
        Guid? competitionCategoryId = null,
        string? styleCode = StyleCodeHazyIpa,
        string submittedAt = "2025-09-01T09:21:16Z",
        decimal abvPercent = 7.6m,
        string? brewDate = "2025-08-12",
        string? bottlingDate = "2025-08-28",
        string? malts = "Pale Ale, Trigo",
        string? hops = "Citra, Mosaic",
        string? yeast = "White Lab WL-001-P",
        string? otherIngredients = null,
        string? entryInstructions = null,
        string? beerName = null) => new
        {
            participantName,
            participantEmail,
            acceMemberNumber,
            dateOfBirth,
            phone,
            competitionCategoryId,
            styleCode,
            submittedAt,
            abvPercent,
            brewDate,
            bottlingDate,
            malts,
            hops,
            yeast,
            otherIngredients,
            entryInstructions,
            beerName,
        };

    [Fact]
    public async Task EditRowWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row(style: $"{UnknownStyleCode}. X"));

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await EditRowAsync(judge, competitionId, importId, rowNumber: 1, FullEditBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EditRowResolvingAStyleMismatchLetsConsolidationIncludeIt()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var categoryId = await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row(style: $"{UnknownStyleCode}. Nonexistent"));

        var edit = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1,
            FullEditBody(competitionCategoryId: categoryId, styleCode: StyleCodeStout));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var editDocument = JsonDocument.Parse(await edit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Valid", editDocument.RootElement.GetProperty("status").GetString());

        var consolidate = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);
        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal(StyleCodeStout, entries[0].GetProperty("styleCode").GetString());
    }

    [Fact]
    public async Task EditRowResolvingACategoryMismatchLetsConsolidationIncludeIt()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var categoryId = await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row(category: "Estilos experimentales"));

        var edit = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1,
            FullEditBody(competitionCategoryId: categoryId, styleCode: StyleCodeHazyIpa));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var editDocument = JsonDocument.Parse(await edit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Valid", editDocument.RootElement.GetProperty("status").GetString());

        var consolidate = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);
    }

    [Fact]
    public async Task EditRowLeavingTheCategoryUnresolvedRecomputesStatusInvalid()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row(category: "Estilos experimentales"));

        var edit = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1,
            FullEditBody(competitionCategoryId: null, styleCode: StyleCodeHazyIpa));

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var document = JsonDocument.Parse(await edit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Invalid", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task EditRowWithACompetitionCategoryIdFromADifferentCompetitionReturns400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());

        var otherCompetitionId = await CreateCompetitionAsync(organizer, "OtherImport");
        var foreignCategoryId = await SetStandardCategoryAsync(organizer, otherCompetitionId);

        var response = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1,
            FullEditBody(competitionCategoryId: foreignCategoryId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EditRowWithAStyleCodeNotInTheCatalogReturns400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var categoryId = await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());

        var response = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1,
            FullEditBody(competitionCategoryId: categoryId, styleCode: "NOT-A-REAL-CODE"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EditRowCanSetAnOrganizerTypedBeerName()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var categoryId = await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());

        var response = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1,
            FullEditBody(competitionCategoryId: categoryId, beerName: "Hop Cannon"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Hop Cannon", document.RootElement.GetProperty("data").GetProperty("beerName").GetString());
    }

    [Fact]
    public async Task EditRowOnAnExcludedRowReturns400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var categoryId = await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());
        await ExcludeRowAsync(organizer, competitionId, importId, rowNumber: 1);

        var response = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1,
            FullEditBody(competitionCategoryId: categoryId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task EditRowSettingADisallowedCategoryStylePairRecomputesStatusCategoryStyleMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var categoryId = await SetStandardCategoryAsync(organizer, competitionId, styleCodes: [StyleCodeHazyIpa]);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());

        var edit = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1,
            FullEditBody(competitionCategoryId: categoryId, styleCode: StyleCodeStout));

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var document = JsonDocument.Parse(await edit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("CategoryStyleMismatch", document.RootElement.GetProperty("status").GetString());
        Assert.True(document.RootElement.TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
    }
}
