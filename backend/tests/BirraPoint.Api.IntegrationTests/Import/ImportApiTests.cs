using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Import;

/// <summary>
/// T032: HTTP-level contract tests for the Entry Import slice (contracts/rest-api.md §Entry
/// Import, contracts/import-file.md) against the ACCE club's Spanish-header `.xlsx` format —
/// upload → row results, the full-row-edit PUT, the exclude action, consolidation gating, and the
/// file-level `400 invalid-import-file` rejections.
/// </summary>
public sealed class ImportApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // BJCP 2021 seed data (T010): real catalog entries used throughout.
    private const string StyleCodeHazyIpa = "21C";
    private const string StyleCodeStout = "20C";
    private const string UnknownStyleCode = "99Z";
    private const string ClassicStylesCategory = "Estilos clásicos";

    private static readonly string[] StandardHeaders =
    [
        "Marca temporal",
        "Dirección de correo electrónico",
        "Numero socio ACCE",
        "Nombre y apellidos",
        "Fecha de nacimiento",
        "Teléfono",
        "Categoria",
        "Estilo",
        "Grado alcohol: (%)",
        "Número de botellas enviadas",
        "Fecha de elaboración",
        "Fecha de embotellado",
        "Maltas utilizadas",
        "Lupulos utilizados",
        "Levadura utilizada",
        "Otros ingredientes",
        "Instrucciones de entrada",
    ];

    private HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "JUDGE"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Import")
    {
        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = $"{namePrefix} {Guid.NewGuid():N}",
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> TransitionToActiveAsync(HttpClient client, Guid competitionId) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Active" });

    /// <summary>Configures this competition's wizard-step-3 categories (Import validates Categoria
    /// against these) and returns the created category's id.</summary>
    private static async Task<Guid> SetStandardCategoryAsync(
        HttpClient organizer, Guid competitionId, string categoryName = ClassicStylesCategory,
        params string[] styleCodes)
    {
        var codes = styleCodes.Length > 0 ? styleCodes : [StyleCodeHazyIpa, StyleCodeStout];
        var response = await organizer.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = categoryName, displayOrder = 0, styleCodes = codes } },
        });
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("categories")[0].GetProperty("id").GetGuid();
    }

    /// <summary>Builds an in-memory ACCE `.xlsx` (ClosedXML — already a BirraPoint.Api dependency)
    /// with an arbitrary header/row layout; row cells carry their real typed value (DateTime/
    /// double/string), matching how the organizer's real file reads out of Excel.</summary>
    private static byte[] BuildWorkbook(string[] headers, IEnumerable<object?[]> rows, bool includeHeaderRow = true)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Entries");

        if (includeHeaderRow)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                worksheet.Cell(1, col + 1).Value = headers[col];
            }
        }

        var rowIndex = includeHeaderRow ? 2 : 1;
        foreach (var row in rows)
        {
            for (var col = 0; col < row.Length; col++)
            {
                SetCellValue(worksheet.Cell(rowIndex, col + 1), row[col]);
            }

            rowIndex++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case DateTime dt:
                cell.Value = dt;
                break;
            case double d:
                cell.Value = d;
                break;
            case int i:
                cell.Value = (double)i;
                break;
            case string s:
                cell.Value = s;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported test cell value type.");
        }
    }

    /// <summary>Builds one ACCE row in standard column order, defaulting every field to a
    /// well-formed value so a test only needs to override what it cares about.</summary>
    private static object?[] Row(
        DateTime? submittedAt = null,
        string? email = "dezaprieto@gmail.com",
        double? acceMemberNumber = 1423.0,
        string? name = "José Deza Prieto",
        DateTime? dateOfBirth = null,
        object? phone = null,
        string? category = ClassicStylesCategory,
        string? style = "21C. Hazy IPA",
        double? abv = 7.6,
        int? bottles = 3,
        DateTime? brewDate = null,
        DateTime? bottlingDate = null,
        string? malts = "Pale Ale, Trigo, Copos de avena, Melanoidin",
        string? hops = "Amarillo, Citra, Mosaic",
        string? yeast = "White Lab WL-001-P California Ale",
        string? otherIngredients = null,
        string? entryInstructions = null) =>
    [
        submittedAt ?? new DateTime(2025, 9, 1, 9, 21, 16, DateTimeKind.Unspecified),
        email,
        acceMemberNumber,
        name,
        dateOfBirth,
        phone ?? 699989612.0,
        category,
        style,
        abv,
        bottles,
        brewDate ?? new DateTime(2025, 8, 12, 0, 0, 0, DateTimeKind.Unspecified),
        bottlingDate ?? new DateTime(2025, 8, 28, 0, 0, 0, DateTimeKind.Unspecified),
        malts,
        hops,
        yeast,
        otherIngredients,
        entryInstructions,
    ];

    private static byte[] BuildAcceWorkbook(params object?[][] rows) => BuildWorkbook(StandardHeaders, rows);

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid competitionId, byte[] xlsxBytes, string fileName = "entries.xlsx", string? contentType = null)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(xlsxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            contentType ?? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);
        return await client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports"), content);
    }

    private static async Task<(Guid ImportId, List<JsonElement> Rows)> UploadAcceRowsAsync(
        HttpClient client, Guid competitionId, params object?[][] rows)
    {
        var response = await UploadAsync(client, competitionId, BuildAcceWorkbook(rows));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var importId = document.RootElement.GetProperty("importId").GetGuid();
        var parsedRows = document.RootElement.GetProperty("rows").Clone().EnumerateArray().ToList();
        return (importId, parsedRows);
    }

    private static Task<HttpResponseMessage> GetImportAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports/{importId}"));

    private static Task<HttpResponseMessage> EditRowAsync(
        HttpClient client, Guid competitionId, Guid importId, int rowNumber, object body) =>
        client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/imports/{importId}/rows/{rowNumber}", body);

    private static Task<HttpResponseMessage> ExcludeRowAsync(
        HttpClient client, Guid competitionId, Guid importId, int rowNumber) =>
        client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports/{importId}/rows/{rowNumber}/exclude"), null);

    private static Task<HttpResponseMessage> ConsolidateAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports/{importId}/consolidate"), null);

    private static Task<HttpResponseMessage> RevalidateAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports/{importId}/revalidate"), null);

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

    // ---- Upload: auth & ownership ----------------------------------------------------------

    [Fact]
    public async Task UploadWithoutABearerTokenIsRejectedWith401()
    {
        using var client = factory.CreateClient();

        var response = await UploadAsync(client, Guid.NewGuid(), BuildAcceWorkbook());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await UploadAsync(judge, competitionId, BuildAcceWorkbook());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UploadForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await UploadAsync(other, competitionId, BuildAcceWorkbook());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Upload: row-level parsing (contracts/import-file.md) -------------------------------

    [Fact]
    public async Task UploadWithAValidRowReturns201WithStatusValidAndTheFullDataEnvelope()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        var (importId, rows) = await UploadAcceRowsAsync(organizer, competitionId, Row());

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

        var (_, rows) = await UploadAcceRowsAsync(organizer, competitionId, Row(style: $"{UnknownStyleCode}. Nonexistent Style"));

        Assert.Single(rows);
        Assert.Equal("StyleMismatch", rows[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task UploadRowWithACategoryThatIsNotConfiguredForThisCompetitionIsMarkedCategoryMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        var (_, rows) = await UploadAcceRowsAsync(organizer, competitionId, Row(category: "Estilos experimentales"));

        Assert.Single(rows);
        Assert.Equal("CategoryMismatch", rows[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task UploadRowMissingARequiredCellIsMarkedInvalidWithAnErrorMessage()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        var (_, rows) = await UploadAcceRowsAsync(organizer, competitionId, Row(email: null));

        Assert.Single(rows);
        Assert.Equal("Invalid", rows[0].GetProperty("status").GetString());
        Assert.True(rows[0].TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
    }

    // ---- Upload: file-level rejections (400 invalid-import-file) ----------------------------

    [Fact]
    public async Task UploadANonXlsxFileReturns400InvalidImportFile()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var response = await UploadAsync(
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

        var response = await UploadAsync(organizer, competitionId, xlsx);

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

        var response = await UploadAsync(organizer, competitionId, BuildAcceWorkbook(Row()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    // ---- Upload: single active batch per competition -----------------------------------------

    [Fact]
    public async Task UploadingASecondFileDiscardsThePriorUnconsolidatedBatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        // First batch: two valid rows, left unconsolidated.
        await UploadAcceRowsAsync(organizer, competitionId,
            Row(email: "ana@brew.example", name: "Ana Gomez"),
            Row(email: "luis@brew.example", name: "Luis Perez", style: "20C. Imperial Stout"));

        // Second batch: a single valid row.
        var (secondImportId, secondRows) = await UploadAcceRowsAsync(organizer, competitionId,
            Row(email: "sam@brew.example", name: "Sam Roe"));
        Assert.Single(secondRows);

        var consolidate = await ConsolidateAsync(organizer, competitionId, secondImportId);
        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);

        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // Only the second (active) batch's row landed — if the first batch had not been
        // discarded, this would be 3.
        Assert.Equal(1, document.RootElement.GetProperty("imported").GetInt32());
    }

    // ---- GET current row states --------------------------------------------------------------

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

    // ---- Full row edit (EditImportRow) -------------------------------------------------------

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

    // ---- FR-053: category/style allow-list (CompetitionCategoryStyle) -----------------------

    [Fact]
    public async Task UploadRowWithAStyleValidInTheCatalogButNotAssignedToTheResolvedCategoryIsMarkedCategoryStyleMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        // Only StyleCodeHazyIpa is assigned to the category — StyleCodeStout is BJCP-valid but not
        // part of this competition's allow-list for it.
        await SetStandardCategoryAsync(organizer, competitionId, styleCodes: [StyleCodeHazyIpa]);

        var (_, rows) = await UploadAcceRowsAsync(organizer, competitionId, Row(style: "20C. Imperial Stout"));

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

    [Fact]
    public async Task ConsolidateIsBlockedWith409WhileARowIsCategoryStyleMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId, styleCodes: [StyleCodeHazyIpa]);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row(style: "20C. Imperial Stout"));

        var response = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:unresolved-import-rows", document.RootElement.GetProperty("type").GetString());
    }

    // ---- Exclude a row -------------------------------------------------------------------------

    [Fact]
    public async Task ExcludeRowWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row(style: $"{UnknownStyleCode}. X"));

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ExcludeRowAsync(judge, competitionId, importId, rowNumber: 1);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExcludeRowSetsStatusExcludedAndItIsNoLongerAConsolidationBlocker()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row(style: $"{UnknownStyleCode}. X"));

        var exclude = await ExcludeRowAsync(organizer, competitionId, importId, rowNumber: 1);
        Assert.Equal(HttpStatusCode.OK, exclude.StatusCode);
        using var excludeDocument = JsonDocument.Parse(await exclude.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Excluded", excludeDocument.RootElement.GetProperty("status").GetString());

        var consolidate = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);
        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, document.RootElement.GetProperty("imported").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("excluded").GetInt32());
    }

    // ---- Consolidate -----------------------------------------------------------------------

    [Fact]
    public async Task ConsolidateWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ConsolidateAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConsolidateIsBlockedWith409WhileAnyRowIsUnresolved()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId,
            Row(email: "ana@brew.example", name: "Ana Gomez"),
            Row(email: "sam@brew.example", name: "Sam Roe", style: $"{UnknownStyleCode}. X"));

        var response = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:unresolved-import-rows", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConsolidateIsBlockedWith409WhileARowIsCategoryMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row(category: "Estilos experimentales"));

        var response = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:unresolved-import-rows", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConsolidateSuccessPopulatesEveryNewBeerEntryFieldAndSetsTheCategory()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var categoryId = await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());

        var response = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, document.RootElement.GetProperty("imported").GetInt32());
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();
        var entryId = entries[0].GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.BeerEntries.SingleAsync(e => e.Id == entryId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(categoryId, entry.CompetitionCategoryId);
        Assert.Equal(StyleCodeHazyIpa, entry.StyleCode);
        Assert.Equal(7.6m, entry.AbvPercent);
        Assert.Equal(new DateOnly(2025, 8, 12), entry.BrewDate);
        Assert.Equal(new DateOnly(2025, 8, 28), entry.BottlingDate);
        Assert.Equal("Pale Ale, Trigo, Copos de avena, Melanoidin", entry.Malts);
        Assert.Equal("Amarillo, Citra, Mosaic", entry.Hops);
        Assert.Equal("White Lab WL-001-P California Ale", entry.Yeast);
        Assert.Null(entry.BeerName);

        var participant = await db.Participants.SingleAsync(p => p.Id == entry.ParticipantId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("1423", participant.AcceMemberNumber);
        Assert.Equal("699989612", participant.Phone);
    }

    [Fact]
    public async Task ConsolidateASecondTimeOnTheSameBatchIsRejectedWith409AndDoesNotDuplicateEntries()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(organizer, competitionId, Row());

        var first = await ConsolidateAsync(organizer, competitionId, importId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var document = JsonDocument.Parse(await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConsolidateForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        await SetStandardCategoryAsync(owner, competitionId);
        var (importId, _) = await UploadAcceRowsAsync(owner, competitionId, Row());

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await ConsolidateAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConsolidateReimportingTheSameEmailWithinOneCompetitionUpdatesTheExistingParticipant()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);

        var (firstImportId, _) = await UploadAcceRowsAsync(organizer, competitionId,
            Row(email: "dezaprieto@gmail.com", name: "Jose Deza", acceMemberNumber: 1000.0));
        await ConsolidateAsync(organizer, competitionId, firstImportId);

        var (secondImportId, _) = await UploadAcceRowsAsync(organizer, competitionId,
            Row(email: "dezaprieto@gmail.com", name: "José Deza Prieto (updated)", acceMemberNumber: 2000.0,
                style: "20C. Imperial Stout"));
        var consolidate = await ConsolidateAsync(organizer, competitionId, secondImportId);
        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var participants = await db.Participants
            .Where(p => p.CompetitionId == competitionId && p.Email == "dezaprieto@gmail.com")
            .ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(participants);
        Assert.Equal("José Deza Prieto (updated)", participants[0].Name);
        Assert.Equal("2000", participants[0].AcceMemberNumber);

        var entryCount = await db.BeerEntries.CountAsync(e => e.ParticipantId == participants[0].Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, entryCount);
    }

    [Fact]
    public async Task ConsolidateTheSameParticipantEmailInADifferentCompetitionCreatesAnIndependentParticipantRow()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var firstCompetitionId = await CreateCompetitionAsync(organizer, "ImportFirst");
        await SetStandardCategoryAsync(organizer, firstCompetitionId);
        var (firstImportId, _) = await UploadAcceRowsAsync(organizer, firstCompetitionId,
            Row(email: "shared@brew.example", name: "Name In First Competition"));
        await ConsolidateAsync(organizer, firstCompetitionId, firstImportId);

        var secondCompetitionId = await CreateCompetitionAsync(organizer, "ImportSecond");
        await SetStandardCategoryAsync(organizer, secondCompetitionId);
        var (secondImportId, _) = await UploadAcceRowsAsync(organizer, secondCompetitionId,
            Row(email: "shared@brew.example", name: "Name In Second Competition"));
        await ConsolidateAsync(organizer, secondCompetitionId, secondImportId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var firstParticipant = await db.Participants
            .SingleAsync(p => p.CompetitionId == firstCompetitionId && p.Email == "shared@brew.example", cancellationToken: TestContext.Current.CancellationToken);
        var secondParticipant = await db.Participants
            .SingleAsync(p => p.CompetitionId == secondCompetitionId && p.Email == "shared@brew.example", cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(firstParticipant.Id, secondParticipant.Id);
        Assert.Equal("Name In First Competition", firstParticipant.Name);
        Assert.Equal("Name In Second Competition", secondParticipant.Name);
    }

    // ---- Revalidate (RevalidateImport, FR-054) -----------------------------------------------

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
