using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;
using ClosedXML.Excel;

namespace BirraPoint.Api.IntegrationTests.Import;

/// <summary>
/// T032: HTTP-level contract tests for the Entry Import slices (contracts/rest-api.md §Entry
/// Import, contracts/import-file.md) — upload → row results, row resolution, consolidation
/// gating, and the file-level `400 invalid-import-file` rejections. Written against the contract
/// ahead of the implementation (T031/T033-T035, in progress in parallel); failures against
/// non-existent routes are expected until that work lands.
/// </summary>
public sealed class ImportApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // BJCP 2021 seed data (T010): 21A/American IPA and 20C/Imperial Stout are real catalog
    // entries used by the contract's own worked example (import-file.md); 99Z is the contract's
    // own example of a code with no catalog match.
    private const string StyleCodeApa = "21A";
    private const string StyleNameApa = "American IPA";
    private const string StyleCodeStout = "20C";
    private const string UnknownStyleCode = "99Z";

    private static readonly string[] StandardHeaders =
        ["ParticipantName", "ParticipantEmail", "BeerName", "Style", "Collaborators"];

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

    /// <summary>Builds an in-memory .xlsx (ClosedXML — already a BirraPoint.Api dependency) with an arbitrary header/row layout.</summary>
    private static byte[] BuildWorkbook(string[] headers, IEnumerable<string?[]> rows, bool includeHeaderRow = true)
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
                if (row[col] is not null)
                {
                    worksheet.Cell(rowIndex, col + 1).Value = row[col];
                }
            }

            rowIndex++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildStandardWorkbook(
        params (string ParticipantName, string ParticipantEmail, string BeerName, string Style, string? Collaborators)[] rows) =>
        BuildWorkbook(
            StandardHeaders,
            rows.Select(row => new[] { row.ParticipantName, row.ParticipantEmail, row.BeerName, row.Style, row.Collaborators }));

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid competitionId, byte[] xlsxBytes, string fileName = "entries.xlsx", string? contentType = null)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(xlsxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            contentType ?? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);
        return await client.PostAsync($"/api/v1/competitions/{competitionId}/imports", content);
    }

    private static async Task<(Guid ImportId, List<JsonElement> Rows)> UploadStandardWorkbookAsync(
        HttpClient client, Guid competitionId,
        params (string ParticipantName, string ParticipantEmail, string BeerName, string Style, string? Collaborators)[] rows)
    {
        var response = await UploadAsync(client, competitionId, BuildStandardWorkbook(rows));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var importId = document.RootElement.GetProperty("importId").GetGuid();
        var parsedRows = document.RootElement.GetProperty("rows").Clone().EnumerateArray().ToList();
        return (importId, parsedRows);
    }

    private static Task<HttpResponseMessage> GetImportAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.GetAsync($"/api/v1/competitions/{competitionId}/imports/{importId}");

    private static Task<HttpResponseMessage> ResolveRowAsync(
        HttpClient client, Guid competitionId, Guid importId, int rowNumber, object body) =>
        client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/imports/{importId}/rows/{rowNumber}", body);

    private static Task<HttpResponseMessage> ConsolidateAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.PostAsync($"/api/v1/competitions/{competitionId}/imports/{importId}/consolidate", null);

    // ---- Upload: auth & ownership ----------------------------------------------------------

    [Fact]
    public async Task Upload_without_a_bearer_token_is_rejected_with_401()
    {
        using var client = factory.CreateClient();

        var response = await UploadAsync(client, Guid.NewGuid(), BuildStandardWorkbook());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await UploadAsync(judge, competitionId, BuildStandardWorkbook());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await UploadAsync(other, competitionId, BuildStandardWorkbook());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Upload: row-level parsing (contracts/import-file.md) -------------------------------

    [Fact]
    public async Task Upload_with_valid_rows_returns_201_with_all_rows_Valid()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var (importId, rows) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, "luis@brew.example"),
            ("Luis Perez", "luis@brew.example", "Dark Matter", StyleNameApa, null));

        Assert.NotEqual(Guid.Empty, importId);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("Valid", row.GetProperty("status").GetString()));

        // Row order/number and the "data" envelope both round-trip; contract only fixes the
        // outer shape ({ rowNumber, status, data, error? }), so we assert presence/type rather
        // than an exact schema for "data".
        Assert.Equal(1, rows[0].GetProperty("rowNumber").GetInt32());
        Assert.Equal(2, rows[1].GetProperty("rowNumber").GetInt32());
        Assert.All(rows, row => Assert.Equal(JsonValueKind.Object, row.GetProperty("data").ValueKind));
    }

    [Fact]
    public async Task Upload_headers_are_matched_case_insensitively_and_column_order_is_not_significant()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        // Reordered + mixed-case headers, no Collaborators column at all (optional).
        var xlsx = BuildWorkbook(
            headers: ["style", "BEERNAME", "participantemail", "ParticipantName"],
            rows: [["21A", "Hop Cannon", "ana@brew.example", "Ana Gomez"]]);

        var response = await UploadAsync(organizer, competitionId, xlsx);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        Assert.Equal("Valid", rows[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Upload_row_with_a_style_that_does_not_match_the_catalog_is_marked_StyleMismatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var (_, rows) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Sam Roe", "sam@brew.example", "Fizzy Lifting", UnknownStyleCode, null));

        Assert.Single(rows);
        Assert.Equal("StyleMismatch", rows[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Upload_row_missing_a_required_cell_is_marked_Invalid_with_an_error_message()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        // ParticipantEmail is required (import-file.md) but left empty.
        var (_, rows) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Ana Gomez", "", "Hop Cannon", StyleCodeApa, null));

        Assert.Single(rows);
        Assert.Equal("Invalid", rows[0].GetProperty("status").GetString());
        Assert.True(rows[0].TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
    }

    [Fact]
    public async Task Upload_duplicate_participant_email_and_beer_name_pair_marks_the_second_occurrence_Invalid()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var (_, rows) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, null),
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, null));

        Assert.Equal(2, rows.Count);
        Assert.Equal("Valid", rows[0].GetProperty("status").GetString());
        Assert.Equal("Invalid", rows[1].GetProperty("status").GetString());
    }

    // ---- Upload: file-level rejections (400 invalid-import-file) ----------------------------

    [Fact]
    public async Task Upload_a_non_xlsx_file_returns_400_invalid_import_file()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var response = await UploadAsync(
            organizer, competitionId, "not an xlsx file"u8.ToArray(), fileName: "entries.txt", contentType: "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Upload_an_unreadable_xlsx_file_returns_400_invalid_import_file()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var corruptBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var response = await UploadAsync(organizer, competitionId, corruptBytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Upload_a_workbook_with_zero_data_rows_returns_400_invalid_import_file()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var xlsx = BuildWorkbook(StandardHeaders, rows: []);
        var response = await UploadAsync(organizer, competitionId, xlsx);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Upload_a_workbook_missing_a_required_header_column_returns_400_invalid_import_file()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        // No "Style" column at all.
        var xlsx = BuildWorkbook(
            headers: ["ParticipantName", "ParticipantEmail", "BeerName"],
            rows: [["Ana Gomez", "ana@brew.example", "Hop Cannon"]]);

        var response = await UploadAsync(organizer, competitionId, xlsx);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Upload_while_the_competition_is_in_evaluation_is_rejected_with_409()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await TransitionToActiveAsync(organizer, competitionId);
        await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "InEvaluation" });

        var response = await UploadAsync(organizer, competitionId, BuildStandardWorkbook(
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, null)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    // ---- Upload: single active batch per competition -----------------------------------------

    [Fact]
    public async Task Uploading_a_second_file_discards_the_prior_unconsolidated_batch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        // First batch: two valid rows, left unconsolidated.
        await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, null),
            ("Luis Perez", "luis@brew.example", "Dark Matter", StyleCodeStout, null));

        // Second batch: a single valid row.
        var (secondImportId, secondRows) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Sam Roe", "sam@brew.example", "Fizzy Lifting", StyleCodeApa, null));
        Assert.Single(secondRows);

        var consolidate = await ConsolidateAsync(organizer, competitionId, secondImportId);
        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);

        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync());
        // Only the second (active) batch's row landed — if the first batch had not been
        // discarded, this would be 3.
        Assert.Equal(1, document.RootElement.GetProperty("imported").GetInt32());
    }

    // ---- GET current row states --------------------------------------------------------------

    [Fact]
    public async Task Get_import_returns_404_for_a_competition_owned_by_a_different_organizer()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var (importId, _) = await UploadStandardWorkbookAsync(owner, competitionId,
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, null));

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetImportAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_import_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, null));

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetImportAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_import_returns_the_current_row_states()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Sam Roe", "sam@brew.example", "Fizzy Lifting", UnknownStyleCode, null));

        await ResolveRowAsync(organizer, competitionId, importId, rowNumber: 1,
            body: new { action = "exclude" });

        var response = await GetImportAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rows = document.RootElement.GetProperty("rows").EnumerateArray().ToList();
        Assert.Single(rows);
        // A resolved (excluded) row must no longer read as StyleMismatch/Invalid, since those
        // are exactly the statuses that block consolidation (FR-011) — the contract does not
        // name the post-exclusion status literal, so we assert on that observable property
        // instead of guessing an exact string.
        var status = rows[0].GetProperty("status").GetString();
        Assert.NotEqual("StyleMismatch", status);
        Assert.NotEqual("Invalid", status);
    }

    // ---- Resolve a row -------------------------------------------------------------------------

    [Fact]
    public async Task Resolve_row_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Sam Roe", "sam@brew.example", "Fizzy Lifting", UnknownStyleCode, null));

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ResolveRowAsync(judge, competitionId, importId, rowNumber: 1,
            body: new { action = "exclude" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_row_assign_style_with_a_code_not_in_the_catalog_returns_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Sam Roe", "sam@brew.example", "Fizzy Lifting", UnknownStyleCode, null));

        var response = await ResolveRowAsync(organizer, competitionId, importId, rowNumber: 1,
            body: new { action = "assign-style", styleCode = "NOT-A-REAL-CODE" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_row_assign_style_with_a_valid_catalog_code_lets_consolidation_include_it()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Sam Roe", "sam@brew.example", "Fizzy Lifting", UnknownStyleCode, null));

        var resolve = await ResolveRowAsync(organizer, competitionId, importId, rowNumber: 1,
            body: new { action = "assign-style", styleCode = StyleCodeStout });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);

        var consolidate = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);
        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync());
        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Assert.Single(entries);
        Assert.Equal(StyleCodeStout, entries[0].GetProperty("styleCode").GetString());
    }

    // ---- Consolidate -----------------------------------------------------------------------

    [Fact]
    public async Task Consolidate_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, null));

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ConsolidateAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Consolidate_is_blocked_with_409_while_any_row_is_unresolved()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, null),
            ("Sam Roe", "sam@brew.example", "Fizzy Lifting", UnknownStyleCode, null));

        var response = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:unresolved-import-rows", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Consolidate_success_returns_imported_and_excluded_counts_with_unique_blind_codes()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadStandardWorkbookAsync(organizer, competitionId,
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, null),
            ("Luis Perez", "luis@brew.example", "Dark Matter", StyleCodeStout, null),
            ("Sam Roe", "sam@brew.example", "Fizzy Lifting", UnknownStyleCode, null));

        // Resolve the one non-Valid row by excluding it, so consolidation is unblocked.
        await ResolveRowAsync(organizer, competitionId, importId, rowNumber: 3, body: new { action = "exclude" });

        var response = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, document.RootElement.GetProperty("imported").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("excluded").GetInt32());

        var entries = document.RootElement.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.NotEqual(Guid.Empty, entry.GetProperty("id").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("blindCode").GetString()));
        });

        var blindCodes = entries.Select(entry => entry.GetProperty("blindCode").GetString()).ToList();
        Assert.Equal(blindCodes.Distinct().Count(), blindCodes.Count);
    }

    [Fact]
    public async Task Consolidate_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var (importId, _) = await UploadStandardWorkbookAsync(owner, competitionId,
            ("Ana Gomez", "ana@brew.example", "Hop Cannon", StyleCodeApa, null));

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await ConsolidateAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
