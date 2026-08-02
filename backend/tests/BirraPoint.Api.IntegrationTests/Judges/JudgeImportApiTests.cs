using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Judges;

/// <summary>
/// T111: HTTP-level contract tests for the Judge Roster Import slice (contracts/rest-api.md
/// §Judge Roster Import, contracts/judge-import-file.md) — upload → row results, the full-row-edit
/// PUT, the exclude action, consolidation gating (409 unresolved-import-rows while any row is
/// Invalid), successful consolidation creating/updating judges and enqueuing
/// <see cref="DispatchJobType.ProvisionJudgeAccount"/> per judge (never SendInvitation — R-20), and
/// the file-level 400 invalid-import-file rejections.
/// </summary>
public sealed class JudgeImportApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private static readonly string[] StandardHeaders =
    [
        "Nombre y apellidos",
        "Correo electrónico",
        "Rango BJCP",
        "BJCP ID",
        "Categoría preferida",
        "Preferencias",
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

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "JudgeImport")
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

    private static byte[] BuildWorkbook(string[] headers, IEnumerable<object?[]> rows, bool includeHeaderRow = true)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Judges");

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
                if (row[col] is string s)
                {
                    worksheet.Cell(rowIndex, col + 1).Value = s;
                }
            }

            rowIndex++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Builds one judge-roster row in standard column order, defaulting every field to a
    /// well-formed value so a test only needs to override what it cares about. Unlike
    /// Features/Import's test helpers, <paramref name="email"/> defaults to a fixed literal (not
    /// randomized) so passing <c>email: null</c> genuinely leaves the cell blank instead of being
    /// masked by a `??` fallback — tests that need distinct addresses across rows pass them
    /// explicitly.</summary>
    private static object?[] Row(
        string? name = "Rebeca Ruifernández Calzada",
        string? email = "rebeca@brew.example",
        string? bjcpRank = "Certificado",
        string? bjcpId = "E4612",
        string? preferredCategory = "Estilos Clásicos",
        string? preferences = null) =>
    [
        name, email, bjcpRank, bjcpId, preferredCategory, preferences,
    ];

    private static byte[] BuildJudgeWorkbook(params object?[][] rows) => BuildWorkbook(StandardHeaders, rows);

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client, Guid competitionId, byte[] xlsxBytes, string fileName = "judges.xlsx", string? contentType = null)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(xlsxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            contentType ?? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", fileName);
        return await client.PostAsync($"/api/v1/competitions/{competitionId}/judge-imports", content);
    }

    private static async Task<(Guid ImportId, List<JsonElement> Rows)> UploadJudgeRowsAsync(
        HttpClient client, Guid competitionId, params object?[][] rows)
    {
        var response = await UploadAsync(client, competitionId, BuildJudgeWorkbook(rows));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var importId = document.RootElement.GetProperty("importId").GetGuid();
        var parsedRows = document.RootElement.GetProperty("rows").Clone().EnumerateArray().ToList();
        return (importId, parsedRows);
    }

    private static Task<HttpResponseMessage> GetImportAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.GetAsync($"/api/v1/competitions/{competitionId}/judge-imports/{importId}");

    private static Task<HttpResponseMessage> EditRowAsync(
        HttpClient client, Guid competitionId, Guid importId, int rowNumber, object body) =>
        client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/judge-imports/{importId}/rows/{rowNumber}", body);

    private static Task<HttpResponseMessage> ExcludeRowAsync(
        HttpClient client, Guid competitionId, Guid importId, int rowNumber) =>
        client.PostAsync($"/api/v1/competitions/{competitionId}/judge-imports/{importId}/rows/{rowNumber}/exclude", null);

    private static Task<HttpResponseMessage> ConsolidateAsync(HttpClient client, Guid competitionId, Guid importId) =>
        client.PostAsync($"/api/v1/competitions/{competitionId}/judge-imports/{importId}/consolidate", null);

    /// <summary>Full editable-row body matching <c>EditJudgeImportRowRequest</c>'s camelCase wire
    /// shape. Like <see cref="Row"/>, <paramref name="email"/> defaults to a fixed literal so
    /// passing <c>email: null</c> genuinely leaves it blank.</summary>
    private static object FullEditBody(
        string? name = "Rebeca Ruifernández Calzada",
        string? email = "rebeca@brew.example",
        string? bjcpRank = "Certificado",
        string? bjcpId = "E4612",
        string? preferredCategory = "Estilos Clásicos",
        string? preferences = null) => new
        {
            name,
            email,
            bjcpRank,
            bjcpId,
            preferredCategory,
            preferences,
        };

    private async Task<bool> HasDispatchJobAsync(Guid competitionId, DispatchJobType type)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DispatchJobs.AnyAsync(j => j.CompetitionId == competitionId && j.Type == type);
    }

    private async Task WaitForDispatchJobCompletionAsync(Guid competitionId, DispatchJobType type, int expectedCount = 1)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var completedCount = await db.DispatchJobs.CountAsync(j =>
                j.CompetitionId == competitionId && j.Type == type && j.Status == DispatchJobStatus.Completed);

            if (completedCount >= expectedCount)
            {
                return;
            }

            await Task.Delay(PollInterval);
        }

        Assert.Fail($"Timed out waiting for {expectedCount} {type} DispatchJob(s) to complete for competition {competitionId}.");
    }

    // ---- Upload: auth & ownership -----------------------------------------------------------

    [Fact]
    public async Task Upload_without_a_bearer_token_is_rejected_with_401()
    {
        using var client = factory.CreateClient();

        var response = await UploadAsync(client, Guid.NewGuid(), BuildJudgeWorkbook());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await UploadAsync(judge, competitionId, BuildJudgeWorkbook());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await UploadAsync(other, competitionId, BuildJudgeWorkbook());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Upload: row-level parsing (contracts/judge-import-file.md) -------------------------

    [Fact]
    public async Task Upload_with_a_valid_row_returns_201_with_status_Valid_and_the_full_data_envelope()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"rebeca-{Guid.NewGuid():N}@brew.example";

        var (importId, rows) = await UploadJudgeRowsAsync(organizer, competitionId, Row(email: email));

        Assert.NotEqual(Guid.Empty, importId);
        Assert.Single(rows);
        Assert.Equal("Valid", rows[0].GetProperty("status").GetString());
        Assert.Equal(1, rows[0].GetProperty("rowNumber").GetInt32());
        var data = rows[0].GetProperty("data");
        Assert.Equal("Rebeca Ruifernández Calzada", data.GetProperty("name").GetString());
        Assert.Equal(email, data.GetProperty("email").GetString());
        Assert.Equal("Certificado", data.GetProperty("bjcpRank").GetString());
        Assert.Equal("E4612", data.GetProperty("bjcpId").GetString());
        Assert.Equal("Estilos Clásicos", data.GetProperty("preferredCategory").GetString());
    }

    [Fact]
    public async Task Upload_row_missing_email_is_marked_Invalid_with_an_error_message()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var (_, rows) = await UploadJudgeRowsAsync(organizer, competitionId, Row(email: null));

        Assert.Single(rows);
        Assert.Equal("Invalid", rows[0].GetProperty("status").GetString());
        Assert.True(rows[0].TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
    }

    [Fact]
    public async Task Upload_row_missing_name_is_marked_Invalid_with_an_error_message()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var (_, rows) = await UploadJudgeRowsAsync(organizer, competitionId, Row(name: null));

        Assert.Single(rows);
        Assert.Equal("Invalid", rows[0].GetProperty("status").GetString());
        Assert.True(rows[0].TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
    }

    [Fact]
    public async Task Upload_preserves_literal_br_style_text_in_preferences_without_interpreting_it_as_markup()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        const string preferences = "Me gustaría compartir mesa con Aaron Soriano. <br>Un saludo.";

        var (_, rows) = await UploadJudgeRowsAsync(organizer, competitionId, Row(preferences: preferences));

        Assert.Equal(preferences, rows[0].GetProperty("data").GetProperty("preferences").GetString());
    }

    // ---- Upload: file-level rejections (400 invalid-import-file) ----------------------------

    [Fact]
    public async Task Upload_a_non_xlsx_file_returns_400_invalid_import_file()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var response = await UploadAsync(
            organizer, competitionId, "not an xlsx file"u8.ToArray(), fileName: "judges.txt", contentType: "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Upload_a_workbook_missing_a_required_header_column_returns_400_invalid_import_file()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        // No "Correo electrónico" column at all.
        var xlsx = BuildWorkbook(headers: ["Nombre y apellidos"], rows: [["Ana Gomez"]]);

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

        var response = await UploadAsync(organizer, competitionId, BuildJudgeWorkbook(Row()));

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

        await UploadJudgeRowsAsync(organizer, competitionId, Row(name: "Ana Gomez"), Row(name: "Luis Perez"));

        var (secondImportId, secondRows) = await UploadJudgeRowsAsync(organizer, competitionId, Row(name: "Sam Roe"));
        Assert.Single(secondRows);

        var consolidate = await ConsolidateAsync(organizer, competitionId, secondImportId);
        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);

        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync());
        // Only the second (active) batch's row landed — if the first batch had not been
        // discarded, this would be 3.
        Assert.Equal(1, document.RootElement.GetProperty("created").GetArrayLength());
    }

    // ---- GET current row states --------------------------------------------------------------

    [Fact]
    public async Task Get_import_returns_404_for_a_competition_owned_by_a_different_organizer()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var (importId, _) = await UploadJudgeRowsAsync(owner, competitionId, Row());

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetImportAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_import_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row());

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetImportAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Full row edit (EditJudgeImportRow) --------------------------------------------------

    [Fact]
    public async Task Edit_row_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row(email: null));

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await EditRowAsync(judge, competitionId, importId, rowNumber: 1, FullEditBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Edit_row_resolving_a_missing_email_lets_consolidation_include_it()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row(email: null));
        var fixedEmail = $"fixed-{Guid.NewGuid():N}@brew.example";

        var edit = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1, FullEditBody(email: fixedEmail));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var editDocument = JsonDocument.Parse(await edit.Content.ReadAsStringAsync());
        Assert.Equal("Valid", editDocument.RootElement.GetProperty("status").GetString());

        var consolidate = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);
        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync());
        var created = document.RootElement.GetProperty("created").EnumerateArray().ToList();
        Assert.Single(created);
        Assert.Equal(fixedEmail, created[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task Edit_row_leaving_the_email_unresolved_recomputes_status_Invalid()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row(email: null));

        var edit = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1, FullEditBody(email: null));

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var document = JsonDocument.Parse(await edit.Content.ReadAsStringAsync());
        Assert.Equal("Invalid", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Edit_row_on_an_excluded_row_returns_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row());
        await ExcludeRowAsync(organizer, competitionId, importId, rowNumber: 1);

        var response = await EditRowAsync(organizer, competitionId, importId, rowNumber: 1, FullEditBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    // ---- Exclude a row ------------------------------------------------------------------------

    [Fact]
    public async Task Exclude_row_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row(email: null));

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ExcludeRowAsync(judge, competitionId, importId, rowNumber: 1);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Exclude_row_sets_status_Excluded_and_it_is_no_longer_a_consolidation_blocker()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row(email: null));

        var exclude = await ExcludeRowAsync(organizer, competitionId, importId, rowNumber: 1);
        Assert.Equal(HttpStatusCode.OK, exclude.StatusCode);
        using var excludeDocument = JsonDocument.Parse(await exclude.Content.ReadAsStringAsync());
        Assert.Equal("Excluded", excludeDocument.RootElement.GetProperty("status").GetString());

        var consolidate = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);
        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync());
        Assert.Equal(0, document.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(1, document.RootElement.GetProperty("excluded").GetInt32());
    }

    // ---- Consolidate ---------------------------------------------------------------------------

    [Fact]
    public async Task Consolidate_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row());

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ConsolidateAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Consolidate_is_blocked_with_409_unresolved_import_rows_while_any_row_is_Invalid()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(
            organizer, competitionId, Row(name: "Ana Gomez"), Row(name: "Sam Roe", email: null));

        var response = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:unresolved-import-rows", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Consolidate_a_second_time_on_the_same_batch_is_rejected_with_409()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row());

        var first = await ConsolidateAsync(organizer, competitionId, importId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var document = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Consolidate_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var (importId, _) = await UploadJudgeRowsAsync(owner, competitionId, Row());

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await ConsolidateAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Consolidate_success_creates_a_judge_with_every_roster_field_persisted()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"rebeca-{Guid.NewGuid():N}@brew.example";
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row(email: email));

        var response = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var created = document.RootElement.GetProperty("created").EnumerateArray().ToList();
        Assert.Single(created);
        var judgeId = created[0].GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = await db.Judges.SingleAsync(j => j.Id == judgeId);

        Assert.Equal(email, judge.Email);
        Assert.Equal("Rebeca Ruifernández Calzada", judge.DisplayName);
        Assert.Equal("Certificado", judge.BjcpRank);
        Assert.Equal("E4612", judge.BjcpId);
        Assert.Equal("Estilos Clásicos", judge.PreferredCategory);

        var invitation = await db.Invitations.SingleAsync(i => i.JudgeId == judgeId);
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public async Task Consolidate_success_enqueues_ProvisionJudgeAccount_and_never_SendInvitation()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"provision-{Guid.NewGuid():N}@brew.example";
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row(email: email));

        var response = await ConsolidateAsync(organizer, competitionId, importId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var judgeId = document.RootElement.GetProperty("created")[0].GetProperty("id").GetGuid();

        await WaitForDispatchJobCompletionAsync(competitionId, DispatchJobType.ProvisionJudgeAccount);

        Assert.False(await HasDispatchJobAsync(competitionId, DispatchJobType.SendInvitation));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invitation = await db.Invitations.SingleAsync(i => i.JudgeId == judgeId);
        // Provisioning never sends the invitation email — status stays Pending until the
        // organizer's explicit "Notify judges" action (FR-059).
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public async Task Consolidate_reimporting_the_same_email_within_one_competition_updates_the_existing_judge()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"repeat-{Guid.NewGuid():N}@brew.example";

        var (firstImportId, _) = await UploadJudgeRowsAsync(
            organizer, competitionId, Row(name: "Original Name", email: email, bjcpRank: "Pendiente de Rango"));
        var firstConsolidate = await ConsolidateAsync(organizer, competitionId, firstImportId);
        Assert.Equal(HttpStatusCode.OK, firstConsolidate.StatusCode);

        var (secondImportId, _) = await UploadJudgeRowsAsync(
            organizer, competitionId, Row(name: "Updated Name", email: email, bjcpRank: "Certificado"));
        var secondConsolidate = await ConsolidateAsync(organizer, competitionId, secondImportId);

        Assert.Equal(HttpStatusCode.OK, secondConsolidate.StatusCode);
        using var document = JsonDocument.Parse(await secondConsolidate.Content.ReadAsStringAsync());
        Assert.Equal(0, document.RootElement.GetProperty("created").GetArrayLength());
        var updated = document.RootElement.GetProperty("updated").EnumerateArray().ToList();
        Assert.Single(updated);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judges = await db.Judges.Where(j => j.CompetitionId == competitionId && j.Email == email).ToListAsync();
        Assert.Single(judges);
        Assert.Equal("Updated Name", judges[0].DisplayName);
        Assert.Equal("Certificado", judges[0].BjcpRank);
    }

    [Fact]
    public async Task Consolidate_duplicate_emails_within_the_same_file_resolve_to_a_single_upsert()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var sharedEmail = $"shared-{Guid.NewGuid():N}@brew.example";

        var (importId, _) = await UploadJudgeRowsAsync(
            organizer, competitionId,
            Row(name: "First Occurrence", email: sharedEmail),
            Row(name: "Second Occurrence", email: sharedEmail));

        var response = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement.GetProperty("created").GetArrayLength());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judges = await db.Judges.Where(j => j.CompetitionId == competitionId && j.Email == sharedEmail).ToListAsync();
        Assert.Single(judges);
        // Last-import-wins within the batch — the second occurrence's values win.
        Assert.Equal("Second Occurrence", judges[0].DisplayName);
    }

    [Fact]
    public async Task Consolidate_updating_a_judge_created_via_the_email_list_flow_does_not_duplicate_the_invitation()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"already-registered-{Guid.NewGuid():N}@brew.example";

        // Seed via the plain email-list flow (RegisterJudges) first — this already creates an
        // Invitation{Status=Pending} row.
        await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/judges", new { emails = new[] { email } });

        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, Row(name: "Roster Name", email: email));
        var response = await ConsolidateAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, document.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(1, document.RootElement.GetProperty("updated").GetArrayLength());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = await db.Judges.SingleAsync(j => j.CompetitionId == competitionId && j.Email == email);
        var invitations = await db.Invitations.Where(i => i.JudgeId == judge.Id).ToListAsync();
        Assert.Single(invitations);
        Assert.Equal("Roster Name", judge.DisplayName);
    }
}
