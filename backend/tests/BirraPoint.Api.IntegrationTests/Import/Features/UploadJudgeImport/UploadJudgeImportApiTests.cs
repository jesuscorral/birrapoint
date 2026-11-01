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

namespace BirraPoint.Api.IntegrationTests.Import.Features.UploadJudgeImport;

/// <summary>
/// HTTP-level contract tests for POST /competitions/{id}/imports/judges (contracts/rest-api.md §Judge Roster Import, contracts/judge-import-file.md) — auth and ownership, row-level parsing statuses, the file-level 400 invalid-import-file rejections and the single-active-batch rule.
/// </summary>
public sealed class UploadJudgeImportApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task UploadWithoutABearerTokenIsRejectedWith401()
    {
        using var client = Factory.CreateClient();

        var response = await UploadJudgeImportAsync(client, Guid.NewGuid(), BuildJudgeWorkbook());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UploadWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await UploadJudgeImportAsync(judge, competitionId, BuildJudgeWorkbook());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UploadForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await UploadJudgeImportAsync(other, competitionId, BuildJudgeWorkbook());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadWithAValidRowReturns201WithStatusValidAndTheFullDataEnvelope()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"rebeca-{Guid.NewGuid():N}@brew.example";

        var (importId, rows) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(email: email));

        Assert.NotEqual(Guid.Empty, importId);
        Assert.Single(rows);
        Assert.Equal("Valid", rows[0].GetProperty("status").GetString());
        Assert.Equal(1, rows[0].GetProperty("rowNumber").GetInt32());
        var data = rows[0].GetProperty("data");
        Assert.Equal("Ana García Ruiz", data.GetProperty("name").GetString());
        Assert.Equal(email, data.GetProperty("email").GetString());
        Assert.Equal("Certificado", data.GetProperty("bjcpRank").GetString());
        Assert.Equal("E4612", data.GetProperty("bjcpId").GetString());
        Assert.Equal("Estilos Clásicos", data.GetProperty("preferredCategory").GetString());
    }

    [Fact]
    public async Task UploadRowMissingEmailIsMarkedInvalidWithAnErrorMessage()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var (_, rows) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(email: null));

        Assert.Single(rows);
        Assert.Equal("Invalid", rows[0].GetProperty("status").GetString());
        Assert.True(rows[0].TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
    }

    [Fact]
    public async Task UploadRowMissingNameIsMarkedInvalidWithAnErrorMessage()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var (_, rows) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(name: null));

        Assert.Single(rows);
        Assert.Equal("Invalid", rows[0].GetProperty("status").GetString());
        Assert.True(rows[0].TryGetProperty("error", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error.GetString()));
    }

    [Fact]
    public async Task UploadPreservesLiteralBrStyleTextInPreferencesWithoutInterpretingItAsMarkup()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        const string preferences = "Me gustaría compartir mesa con Pablo. <br>Un saludo.";

        var (_, rows) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(preferences: preferences));

        Assert.Equal(preferences, rows[0].GetProperty("data").GetProperty("preferences").GetString());
    }

    [Fact]
    public async Task UploadANonXlsxFileReturns400InvalidImportFile()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var response = await UploadJudgeImportAsync(
            organizer, competitionId, "not an xlsx file"u8.ToArray(), fileName: "judges.txt", contentType: "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UploadAWorkbookMissingARequiredHeaderColumnReturns400InvalidImportFile()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        // No "Correo electrónico" column at all.
        var xlsx = BuildWorkbook(headers: ["Nombre y apellidos"], rows: [["Ana Gomez"]]);

        var response = await UploadJudgeImportAsync(organizer, competitionId, xlsx);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-import-file", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UploadWhileTheCompetitionIsInEvaluationIsRejectedWith409()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await TransitionToActiveAsync(organizer, competitionId);
        await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "InEvaluation" }, cancellationToken: TestContext.Current.CancellationToken);

        var response = await UploadJudgeImportAsync(organizer, competitionId, BuildJudgeWorkbook(JudgeRow()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UploadingASecondFileDiscardsThePriorUnconsolidatedBatch()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(name: "Ana Gomez"), JudgeRow(name: "Luis Perez"));

        var (secondImportId, secondRows) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(name: "Sam Roe"));
        Assert.Single(secondRows);

        var consolidate = await ConsolidateJudgeAsync(organizer, competitionId, secondImportId);
        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);

        using var document = JsonDocument.Parse(await consolidate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // Only the second (active) batch's row landed — if the first batch had not been
        // discarded, this would be 3.
        Assert.Equal(1, document.RootElement.GetProperty("created").GetArrayLength());
    }

    [Fact]
    public async Task UploadOnThePreUnificationJudgeImportsRouteIsNotRoutable()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(BuildJudgeWorkbook(JudgeRow()));
        content.Add(fileContent, "file", "judges.xlsx");
        var response = await organizer.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/judge-imports"), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
