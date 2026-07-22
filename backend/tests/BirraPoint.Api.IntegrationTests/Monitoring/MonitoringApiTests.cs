using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Monitoring;

/// <summary>
/// T068-T069: HTTP-level contract tests for GET /competitions/{id}/progress and
/// GET /competitions/{id}/entries/{entryId}/evaluations (contracts/rest-api.md §Monitoring &amp;
/// audit, FR-038/FR-042) — organizer-only dashboard init and audit drill-down.
/// </summary>
public sealed class MonitoringApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string StyleCodeApa = "21A";
    private const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

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
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, $"{sub}@brew.example", "JUDGE"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Monitoring")
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

    private static string NewBlindCode() => $"B{Guid.NewGuid():N}"[..8];

    private async Task<Guid> SeedParticipantAsync(Guid competitionId, string name, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var participant = new Participant { CompetitionId = competitionId, Name = name, Email = email };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();
        return participant.Id;
    }

    private async Task<Guid> SeedBeerEntryAsync(
        Guid competitionId, Guid participantId, string beerName, string styleCode = StyleCodeApa)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = new BeerEntry
        {
            CompetitionId = competitionId,
            ParticipantId = participantId,
            BeerName = beerName,
            StyleCode = styleCode,
            BlindCode = NewBlindCode(),
        };
        db.BeerEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<Guid> SeedJudgeAsync(Guid competitionId, string email, string keycloakUserId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = new Judge
        {
            CompetitionId = competitionId,
            Email = email,
            DisplayName = email.Split('@')[0],
            KeycloakUserId = keycloakUserId,
        };
        db.Judges.Add(judge);
        await db.SaveChangesAsync();
        return judge.Id;
    }

    private async Task<Guid> SeedTableAsync(Guid competitionId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = new TastingTable { CompetitionId = competitionId, Name = name };
        db.TastingTables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    private async Task SeedTableJudgeAsync(Guid tableId, Guid judgeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableJudges.Add(new TableJudge { TastingTableId = tableId, JudgeId = judgeId });
        await db.SaveChangesAsync();
    }

    private async Task SeedTableSampleAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableSamples.Add(new TableSample { TastingTableId = tableId, BeerEntryId = beerEntryId });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a submitted Evaluation directly, bypassing the SubmitEvaluation sequencing
    /// preconditions — monitoring is a read-only slice over whatever evaluations already exist,
    /// so exercising the submit flow itself is out of scope here (covered by
    /// SubmitEvaluationApiTests/CloseTableApiTests).</summary>
    private async Task<Guid> SeedEvaluationAsync(
        Guid tableId, Guid judgeId, Guid beerEntryId, int aroma = 10, int appearance = 2, int flavor = 15, int mouthfeel = 4, int overall = 8)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evaluation = new Evaluation
        {
            TastingTableId = tableId,
            JudgeId = judgeId,
            BeerEntryId = beerEntryId,
            AromaScore = aroma,
            AppearanceScore = appearance,
            FlavorScore = flavor,
            MouthfeelScore = mouthfeel,
            OverallScore = overall,
            AromaComment = LongComment,
            AppearanceComment = LongComment,
            FlavorComment = LongComment,
            MouthfeelComment = LongComment,
            OverallComment = LongComment,
            Status = EvaluationStatus.Confirmed,
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        db.Evaluations.Add(evaluation);
        await db.SaveChangesAsync();
        return evaluation.Id;
    }

    private static Task<HttpResponseMessage> GetProgressAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync($"/api/v1/competitions/{competitionId}/progress");

    private static Task<HttpResponseMessage> GetEntryEvaluationsAsync(HttpClient client, Guid competitionId, Guid entryId) =>
        client.GetAsync($"/api/v1/competitions/{competitionId}/entries/{entryId}/evaluations");

    private static Task<HttpResponseMessage> CloseAsync(HttpClient client, Guid tableId) =>
        client.PostAsync($"/api/v1/me/tables/{tableId}/close", content: null);

    // ---- GET /progress ----------------------------------------------------------------------------

    [Fact]
    public async Task Get_progress_returns_completed_expected_percent_across_tables()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        // Table A: 1 judge, 2 samples, only 1 evaluation submitted -> 1/2 = 50%.
        var tableAId = await SeedTableAsync(competitionId, "Table A");
        var judgeA1Sub = $"judge-{Guid.NewGuid():N}";
        var judgeA1Id = await SeedJudgeAsync(competitionId, $"{judgeA1Sub}@brew.example", judgeA1Sub);
        await SeedTableJudgeAsync(tableAId, judgeA1Id);

        var participantA1 = await SeedParticipantAsync(competitionId, "Brewer A1", $"a1-{Guid.NewGuid():N}@brew.example");
        var entryA1 = await SeedBeerEntryAsync(competitionId, participantA1, "Beer A1");
        await SeedTableSampleAsync(tableAId, entryA1);

        var participantA2 = await SeedParticipantAsync(competitionId, "Brewer A2", $"a2-{Guid.NewGuid():N}@brew.example");
        var entryA2 = await SeedBeerEntryAsync(competitionId, participantA2, "Beer A2");
        await SeedTableSampleAsync(tableAId, entryA2);

        await SeedEvaluationAsync(tableAId, judgeA1Id, entryA1);

        // Table B: 2 judges, 1 sample, both evaluations submitted -> 2/2 = 100%.
        var tableBId = await SeedTableAsync(competitionId, "Table B");
        var judgeB1Sub = $"judge-{Guid.NewGuid():N}";
        var judgeB1Id = await SeedJudgeAsync(competitionId, $"{judgeB1Sub}@brew.example", judgeB1Sub);
        await SeedTableJudgeAsync(tableBId, judgeB1Id);
        var judgeB2Sub = $"judge-{Guid.NewGuid():N}";
        var judgeB2Id = await SeedJudgeAsync(competitionId, $"{judgeB2Sub}@brew.example", judgeB2Sub);
        await SeedTableJudgeAsync(tableBId, judgeB2Id);

        var participantB1 = await SeedParticipantAsync(competitionId, "Brewer B1", $"b1-{Guid.NewGuid():N}@brew.example");
        var entryB1 = await SeedBeerEntryAsync(competitionId, participantB1, "Beer B1");
        await SeedTableSampleAsync(tableBId, entryB1);

        await SeedEvaluationAsync(tableBId, judgeB1Id, entryB1);
        await SeedEvaluationAsync(tableBId, judgeB2Id, entryB1);

        var response = await GetProgressAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tables = document.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, tables.Count);

        var tableA = tables.Single(t => t.GetProperty("tableId").GetGuid() == tableAId);
        Assert.Equal("Table A", tableA.GetProperty("name").GetString());
        Assert.Equal("Open", tableA.GetProperty("state").GetString());
        Assert.Equal(1, tableA.GetProperty("completed").GetInt32());
        Assert.Equal(2, tableA.GetProperty("expected").GetInt32());
        Assert.Equal(50, tableA.GetProperty("percent").GetInt32());

        var tableB = tables.Single(t => t.GetProperty("tableId").GetGuid() == tableBId);
        Assert.Equal("Table B", tableB.GetProperty("name").GetString());
        Assert.Equal("Open", tableB.GetProperty("state").GetString());
        Assert.Equal(2, tableB.GetProperty("completed").GetInt32());
        Assert.Equal(2, tableB.GetProperty("expected").GetInt32());
        Assert.Equal(100, tableB.GetProperty("percent").GetInt32());
    }

    [Fact]
    public async Task Get_progress_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetProgressAsync(other, competitionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_progress_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetProgressAsync(judge, competitionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- GET /entries/{entryId}/evaluations --------------------------------------------------------

    [Fact]
    public async Task Get_entry_evaluations_returns_judge_names_scores_comments_total_status_and_null_mean_while_open()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedTableAsync(competitionId, "Table A");

        var judge1Sub = $"judge-{Guid.NewGuid():N}";
        var judge1Id = await SeedJudgeAsync(competitionId, $"{judge1Sub}@brew.example", judge1Sub);
        await SeedTableJudgeAsync(tableId, judge1Id);
        var judge2Sub = $"judge-{Guid.NewGuid():N}";
        var judge2Id = await SeedJudgeAsync(competitionId, $"{judge2Sub}@brew.example", judge2Sub);
        await SeedTableJudgeAsync(tableId, judge2Id);

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Secret Beer");
        await SeedTableSampleAsync(tableId, entryId);

        await SeedEvaluationAsync(tableId, judge1Id, entryId, aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8); // total 39
        await SeedEvaluationAsync(tableId, judge2Id, entryId, aroma: 12, appearance: 3, flavor: 20, mouthfeel: 5, overall: 10); // total 50

        var response = await GetEntryEvaluationsAsync(organizer, competitionId, entryId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var evaluations = root.GetProperty("evaluations").EnumerateArray().ToList();
        Assert.Equal(2, evaluations.Count);

        var judge1Item = evaluations.Single(e => e.GetProperty("judgeDisplayName").GetString() == judge1Sub);
        Assert.Equal(10, judge1Item.GetProperty("scores").GetProperty("aroma").GetInt32());
        Assert.Equal(2, judge1Item.GetProperty("scores").GetProperty("appearance").GetInt32());
        Assert.Equal(15, judge1Item.GetProperty("scores").GetProperty("flavor").GetInt32());
        Assert.Equal(4, judge1Item.GetProperty("scores").GetProperty("mouthfeel").GetInt32());
        Assert.Equal(8, judge1Item.GetProperty("scores").GetProperty("overall").GetInt32());
        Assert.Equal(LongComment, judge1Item.GetProperty("comments").GetProperty("aroma").GetString());
        Assert.Equal(39, judge1Item.GetProperty("total").GetInt32());
        Assert.Equal("Confirmed", judge1Item.GetProperty("status").GetString());

        var judge2Item = evaluations.Single(e => e.GetProperty("judgeDisplayName").GetString() == judge2Sub);
        Assert.Equal(50, judge2Item.GetProperty("total").GetInt32());

        Assert.Equal(JsonValueKind.Null, root.GetProperty("consolidatedMean").ValueKind);
    }

    [Fact]
    public async Task Get_entry_evaluations_consolidatedMean_is_populated_after_the_table_closes()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedTableAsync(competitionId, "Table A");

        var judge1Sub = $"judge-{Guid.NewGuid():N}";
        var judge1Id = await SeedJudgeAsync(competitionId, $"{judge1Sub}@brew.example", judge1Sub);
        await SeedTableJudgeAsync(tableId, judge1Id);
        var judge2Sub = $"judge-{Guid.NewGuid():N}";
        var judge2Id = await SeedJudgeAsync(competitionId, $"{judge2Sub}@brew.example", judge2Sub);
        await SeedTableJudgeAsync(tableId, judge2Id);

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Secret Beer");
        await SeedTableSampleAsync(tableId, entryId);

        await SeedEvaluationAsync(tableId, judge1Id, entryId, aroma: 10, appearance: 2, flavor: 15, mouthfeel: 4, overall: 8); // total 39
        await SeedEvaluationAsync(tableId, judge2Id, entryId, aroma: 12, appearance: 3, flavor: 20, mouthfeel: 5, overall: 10); // total 50
        // mean of (39, 50) = 44.5

        using var judgeClient = JudgeClient(judge1Sub);
        var closeResponse = await CloseAsync(judgeClient, tableId);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        var response = await GetEntryEvaluationsAsync(organizer, competitionId, entryId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(44.5m, document.RootElement.GetProperty("consolidatedMean").GetDecimal());
    }

    [Fact]
    public async Task Get_entry_evaluations_for_a_non_owning_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var tableId = await SeedTableAsync(competitionId, "Table A");

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Secret Beer");
        await SeedTableSampleAsync(tableId, entryId);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetEntryEvaluationsAsync(other, competitionId, entryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_entry_evaluations_for_an_entryId_not_belonging_to_the_competition_returns_404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var otherCompetitionId = await CreateCompetitionAsync(organizer, "Other");
        var otherParticipantId = await SeedParticipantAsync(otherCompetitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var otherEntryId = await SeedBeerEntryAsync(otherCompetitionId, otherParticipantId, "Other Beer");

        var response = await GetEntryEvaluationsAsync(organizer, competitionId, otherEntryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_entry_evaluations_for_an_entry_not_yet_assigned_to_a_table_returns_404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Unassigned Beer");

        var response = await GetEntryEvaluationsAsync(organizer, competitionId, entryId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
