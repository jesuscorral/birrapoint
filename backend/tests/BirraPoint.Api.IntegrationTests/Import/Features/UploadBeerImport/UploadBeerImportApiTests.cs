using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Import.Features.UploadBeerImport;

/// <summary>
/// HTTP-level contract tests for POST /competitions/{id}/imports/beers (contracts/rest-api.md §Entry
/// Import, contracts/import-file.md) against the ACCE club's Spanish-header `.xlsx` format — auth and
/// ownership, row-level parsing statuses, the file-level `400 invalid-import-file` rejections and the
/// single-active-batch rule.
/// </summary>
public sealed class UploadBeerImportApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task UploadWithoutABearerTokenIsRejectedWith401()
    {
        using var client = Factory.CreateClient();

        var response = await UploadBeerImportAsync(client, Guid.NewGuid(), BuildAcceWorkbook());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await UploadBeerImportAsync(judge, competitionId, BuildAcceWorkbook());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UploadForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await UploadBeerImportAsync(other, competitionId, BuildAcceWorkbook());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadWithAValidRowReturns201WithStatusValidAndTheFullDataEnvelope()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        var (importId, rows) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow());

        Assert.NotEqual(Guid.Empty, importId);
        Assert.Single(rows);
        Assert.Equal("Valid", rows[0].GetProperty("status").GetString());
        Assert.Equal(1, rows[0].GetProperty("rowNumber").GetInt32());
        var data = rows[0].GetProperty("data");
        Assert.Equal("José Deza Prieto", data.GetProperty("participantName").GetString());
        Assert.Equal("dezaprieto@gmail.com", data.GetProperty("participantEmail").GetString());
        Assert.Equal("1423", data.GetProperty("acceMemberNumber").GetString());
        Assert.Equal("699989612", data.GetProperty("phone").GetString());
        Assert.Equal(StyleCodeHazyIpa, data.GetProperty("resolvedStyleCode").GetString());
        Assert.NotEqual(Guid.Empty, data.GetProperty("competitionCategoryId").GetGuid());
        Assert.Null(data.GetProperty("beerName").GetString());
    }

    [Fact]
    public async Task UploadRowWithAStyleThatDoesNotMatchTheCatalogIsMarkedStyleMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        var (_, rows) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow(style: $"{UnknownStyleCode}. Nonexistent Style"));

        Assert.Single(rows);
        Assert.Equal("StyleMismatch", rows[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task UploadRowWithACategoryThatIsNotConfiguredForThisCompetitionIsMarkedCategoryMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        var (_, rows) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow(category: "Estilos experimentales"));

        Assert.Single(rows);
        Assert.Equal("CategoryMismatch", rows[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task UploadRowMissingARequiredCellIsMarkedInvalidWithAnErrorMessage()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        var (_, rows) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow(email: null));

        Assert.Single(rows);
        Assert.Equal("Invalid", rows[0].GetProperty("status").GetString());
        Assert.True(rows[0].TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
    }

    [Fact]
    public async Task UploadANonXlsxFileReturns400InvalidImportFile()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var response = await UploadBeerImportAsync(
            organizer, competitionId, "not an xlsx file"u8.ToArray(), fileName: "entries.txt", contentType: "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UploadAWorkbookMissingARequiredHeaderColumnReturns400InvalidImportFile()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        // No "Estilo" column at all.
        var xlsx = BuildWorkbook(
            headers: ["Marca temporal", "Dirección de correo electrónico", "Nombre y apellidos", "Categoria", "Grado alcohol: (%)"],
            rows: [[new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Unspecified), "ana@brew.example", "Ana Gomez", ClassicStylesCategory, 5.0]]);

        var response = await UploadBeerImportAsync(organizer, competitionId, xlsx);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UploadWhileTheCompetitionIsInEvaluationIsRejectedWith409()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        await TransitionToActiveAsync(organizer, competitionId);
        await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "InEvaluation" }, cancellationToken: TestContext.Current.CancellationToken);

        var response = await UploadBeerImportAsync(organizer, competitionId, BuildAcceWorkbook(BeerRow()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UploadingASecondFileDiscardsThePriorUnconsolidatedBatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        // First batch: two valid rows, left unconsolidated.
        await UploadBeerRowsAsync(organizer, competitionId,
            BeerRow(email: "ana@brew.example", name: "Ana Gomez"),
            BeerRow(email: "luis@brew.example", name: "Luis Perez", style: "20C. Imperial Stout"));

        // Second batch: a single valid row.
        var (secondImportId, secondRows) = await UploadBeerRowsAsync(organizer, competitionId,
            BeerRow(email: "sam@brew.example", name: "Sam Roe"));
        Assert.Single(secondRows);

        var consolidate = await ConsolidateBeerAsync(organizer, competitionId, secondImportId);
        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);

        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // Only the second (active) batch's row landed — if the first batch had not been
        // discarded, this would be 3.
        Assert.Equal(1, document.RootElement.GetProperty("imported").GetInt32());
    }

    [Fact]
    public async Task UploadRowWithAStyleValidInTheCatalogButNotAssignedToTheResolvedCategoryIsMarkedCategoryStyleMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        // Only StyleCodeHazyIpa is assigned to the category — StyleCodeStout is BJCP-valid but not
        // part of this competition's allow-list for it.
        await SetStandardCategoryAsync(organizer, competitionId, styleCodes: [StyleCodeHazyIpa]);

        var (_, rows) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow(style: "20C. Imperial Stout"));

        Assert.Single(rows);
        Assert.Equal("CategoryStyleMismatch", rows[0].GetProperty("status").GetString());
        var data = rows[0].GetProperty("data");
        // Both individually resolved — only the pairing is rejected.
        Assert.NotEqual(Guid.Empty, data.GetProperty("competitionCategoryId").GetGuid());
        Assert.Equal(StyleCodeStout, data.GetProperty("resolvedStyleCode").GetString());
        Assert.True(rows[0].TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
    }

    [Fact]
    public async Task UploadOnThePreUnificationImportsRouteIsNotRoutable()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(BuildAcceWorkbook(BeerRow()));
        content.Add(fileContent, "file", "entries.xlsx");
        var response = await organizer.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports"), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
