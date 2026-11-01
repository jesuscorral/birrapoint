using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;
using ClosedXML.Excel;

namespace BirraPoint.Api.IntegrationTests.Import;

/// <summary>Shared clients, ACCE workbook builders and request helpers for the Entry Import slice contract tests (contracts/rest-api.md §Entry Import, contracts/import-file.md). Only helpers used by more than one test class live here; the rest stay private in their class.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "Base class of public xUnit test classes; C# requires a base class to be at least as accessible as its derived classes.")]
public abstract class ImportApiTestBase(ApiFactory factory)
{
    private protected ApiFactory Factory => factory;

    // BJCP 2021 seed data (T010): real catalog entries used throughout.
    private protected const string StyleCodeHazyIpa = "21C";
    private protected const string StyleCodeStout = "20C";
    private protected const string UnknownStyleCode = "99Z";
    private protected const string ClassicStylesCategory = "Estilos clásicos";

    private protected static readonly string[] StandardHeaders =
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

    private protected HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private protected HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "JUDGE"));
        return client;
    }

    private protected static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Import")
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

    /// <summary>Configures this competition's wizard-step-3 categories (Import validates Categoria
    /// against these) and returns the created category's id.</summary>
    private protected static async Task<Guid> SetStandardCategoryAsync(
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
    private protected static byte[] BuildWorkbook(string[] headers, IEnumerable<object?[]> rows, bool includeHeaderRow = true)
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

    private protected static void SetCellValue(IXLCell cell, object? value)
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
    private protected static object?[] Row(
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

    private protected static byte[] BuildAcceWorkbook(params object?[][] rows) => BuildWorkbook(StandardHeaders, rows);

    private protected static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid competitionId, byte[] xlsxBytes, string fileName = "entries.xlsx", string? contentType = null)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(xlsxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            contentType ?? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);
        return await client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports"), content);
    }

    private protected static async Task<(Guid ImportId, List<JsonElement> Rows)> UploadAcceRowsAsync(
        HttpClient client, Guid competitionId, params object?[][] rows)
    {
        var response = await UploadAsync(client, competitionId, BuildAcceWorkbook(rows));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var importId = document.RootElement.GetProperty("importId").GetGuid();
        var parsedRows = document.RootElement.GetProperty("rows").Clone().EnumerateArray().ToList();
        return (importId, parsedRows);
    }

    private protected static Task<HttpResponseMessage> ExcludeRowAsync(
        HttpClient client, Guid competitionId, Guid importId, int rowNumber) =>
        client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports/{importId}/rows/{rowNumber}/exclude"), null);

    private protected static Task<HttpResponseMessage> ConsolidateAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/imports/{importId}/consolidate"), null);
}
