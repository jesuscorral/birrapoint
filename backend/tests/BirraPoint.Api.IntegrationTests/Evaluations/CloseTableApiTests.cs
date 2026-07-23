using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Evaluations;

/// <summary>
/// T063: HTTP-level contract tests for POST /me/tables/{tableId}/close (contracts/rest-api.md
/// §Judge workspace, FR-033/FR-042) and PUT /competitions/{id}/evaluations/{evaluationId}
/// (contracts/rest-api.md §Monitoring &amp; audit, FR-035) — the completeness precondition
/// (→ 409 evaluations-incomplete), the double-close guard (→ 409 table-closed), that closing
/// actually flips TastingTable.State so the *existing* SubmitEvaluation table-closed gate engages
/// (FR-034), the correctly-averaged consolidated mean (FR-042), and the organizer's post-close
/// correction (recomputed total/mean, audited, organizer-ownership-scoped 404s, same boundary
/// validation as judge submission).
/// </summary>
public sealed class CloseTableApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
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

    /// <summary>Same email convention as OrderApiTests/SubmitEvaluationApiTests — GetJudgeRecordsAsync
    /// resolves Judge rows by email, not `sub`.</summary>
    private HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, $"{sub}@brew.example", "JUDGE"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Close")
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

    private static Task<HttpResponseMessage> TransitionStateAsync(HttpClient client, Guid competitionId, string target) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target });

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

    /// <summary>Mirrors what POST .../order (FixOrder) would do, without the extra HTTP round-trip
    /// (same convention as SubmitEvaluationApiTests).</summary>
    private async Task FixOrderDirectlyAsync(Guid tableId, Guid judgeId, IReadOnlyList<Guid> orderedEntryIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var table = await db.TastingTables.SingleAsync(t => t.Id == tableId);
        table.OrderFixedByJudgeId = judgeId;
        table.OrderFixedAt = DateTimeOffset.UtcNow;

        var samples = await db.TableSamples.Where(ts => ts.TastingTableId == tableId).ToListAsync();
        for (var i = 0; i < orderedEntryIds.Count; i++)
        {
            samples.Single(s => s.BeerEntryId == orderedEntryIds[i]).SequenceOrder = i + 1;
        }

        await db.SaveChangesAsync();
    }

    private async Task<string> GetBlindCodeAsync(Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.BeerEntries.Where(be => be.Id == beerEntryId).Select(be => be.BlindCode).SingleAsync();
    }

    private async Task<(TableState State, DateTimeOffset? ClosedAt)> GetTableStateAsync(Guid tableId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = await db.TastingTables.SingleAsync(t => t.Id == tableId);
        return (table.State, table.ClosedAt);
    }

    private async Task<AuditLog> GetAuditLogAsync(Guid evaluationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == evaluationId.ToString());
    }

    private static object ValidScores() => new { aroma = 10, appearance = 2, flavor = 15, mouthfeel = 4, overall = 8 };

    private static object ValidComments() => new
    {
        aroma = LongComment,
        appearance = LongComment,
        flavor = LongComment,
        mouthfeel = LongComment,
        overall = LongComment,
    };

    private static Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, Guid tableId, Guid beerEntryId, object? scores = null, object? comments = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/me/tables/{tableId}/evaluations")
        {
            Content = JsonContent.Create(new { beerEntryId, scores = scores ?? ValidScores(), comments = comments ?? ValidComments() }),
        };
        request.Headers.Add("X-Idempotency-Key", $"comp:{tableId}:judge:{beerEntryId}");
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> CloseAsync(HttpClient client, Guid tableId) =>
        client.PostAsync($"/api/v1/me/tables/{tableId}/close", content: null);

    private static Task<HttpResponseMessage> CorrectAsync(
        HttpClient client, Guid competitionId, Guid evaluationId, object? scores = null, object? comments = null) =>
        client.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/evaluations/{evaluationId}",
            new { scores = scores ?? ValidScores(), comments = comments ?? ValidComments() });

    /// <summary>Seeds a competition, one table with <paramref name="sampleCount"/> samples, and
    /// <paramref name="judgeCount"/> actively-assigned judges. Competition state and order-fixed-ness
    /// are left to the caller.</summary>
    private async Task<(Guid CompetitionId, Guid TableId, List<(Guid JudgeId, string JudgeSub)> Judges, List<Guid> EntryIds)>
        SeedTableWithSamplesAsync(HttpClient organizer, int sampleCount = 1, int judgeCount = 1)
    {
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedTableAsync(competitionId, $"Table {Guid.NewGuid():N}");

        var judges = new List<(Guid JudgeId, string JudgeSub)>();
        for (var j = 0; j < judgeCount; j++)
        {
            var judgeSub = $"judge-{Guid.NewGuid():N}";
            var judgeId = await SeedJudgeAsync(competitionId, $"{judgeSub}@brew.example", judgeSub);
            await SeedTableJudgeAsync(tableId, judgeId);
            judges.Add((judgeId, judgeSub));
        }

        var entryIds = new List<Guid>();
        for (var i = 0; i < sampleCount; i++)
        {
            var participantId = await SeedParticipantAsync(
                competitionId, $"Brewer {i}", $"brewer-{Guid.NewGuid():N}@brew.example");
            var entryId = await SeedBeerEntryAsync(competitionId, participantId, $"Secret Beer {i}");
            await SeedTableSampleAsync(tableId, entryId);
            entryIds.Add(entryId);
        }

        return (competitionId, tableId, judges, entryIds);
    }

    /// <summary>Fixture fully ready for a successful submission: competition InEvaluation, order
    /// fixed by the first seeded judge.</summary>
    private async Task<(Guid CompetitionId, Guid TableId, List<(Guid JudgeId, string JudgeSub)> Judges, List<Guid> EntryIds)>
        SeedReadyTableAsync(HttpClient organizer, int sampleCount = 1, int judgeCount = 1)
    {
        var fixture = await SeedTableWithSamplesAsync(organizer, sampleCount, judgeCount);
        await TransitionStateAsync(organizer, fixture.CompetitionId, "Active");
        await TransitionStateAsync(organizer, fixture.CompetitionId, "InEvaluation");
        await FixOrderDirectlyAsync(fixture.TableId, fixture.Judges[0].JudgeId, fixture.EntryIds);
        return fixture;
    }

    // ---- Completeness precondition (FR-033) -----------------------------------------------------

    [Fact]
    public async Task Close_with_incomplete_evaluations_returns_409_evaluations_incomplete_with_missing_blind_codes()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, sampleCount: 2, judgeCount: 1);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        var submitFirst = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0]);
        Assert.Equal(HttpStatusCode.Created, submitFirst.StatusCode);
        // fixture.EntryIds[1] is never submitted.

        var missingBlindCode = await GetBlindCodeAsync(fixture.EntryIds[1]);

        var response = await CloseAsync(judgeClient, fixture.TableId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Contains("evaluations-incomplete", root.GetProperty("type").GetString());
        var missing = root.GetProperty("missing").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal([missingBlindCode], missing);
    }

    // ---- Double-close guard -----------------------------------------------------------------------

    [Fact]
    public async Task Close_a_table_twice_returns_409_table_closed_on_the_second_attempt()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0]);
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);

        var firstClose = await CloseAsync(judgeClient, fixture.TableId);
        Assert.Equal(HttpStatusCode.OK, firstClose.StatusCode);

        var secondClose = await CloseAsync(judgeClient, fixture.TableId);

        Assert.Equal(HttpStatusCode.Conflict, secondClose.StatusCode);
        using var document = JsonDocument.Parse(await secondClose.Content.ReadAsStringAsync());
        Assert.Contains("table-closed", document.RootElement.GetProperty("type").GetString());
    }

    // ---- Happy path: closes successfully, judge response omits organizer-only data (FR-042) -------

    [Fact]
    public async Task Close_succeeds_when_complete_and_the_judge_response_omits_consolidated_scores()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, sampleCount: 1, judgeCount: 2);
        var entryId = fixture.EntryIds[0];

        using var judgeAClient = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeBClient = JudgeClient(fixture.Judges[1].JudgeSub);

        var scoresA = new { aroma = 10, appearance = 2, flavor = 15, mouthfeel = 4, overall = 8 }; // total 39
        var scoresB = new { aroma = 11, appearance = 3, flavor = 17, mouthfeel = 4, overall = 9 }; // total 44, within 7 of scoresA (FR-031) so no discrepancy blocks the close below

        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeAClient, fixture.TableId, entryId, scoresA)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeBClient, fixture.TableId, entryId, scoresB)).StatusCode);

        var closeResponse = await CloseAsync(judgeAClient, fixture.TableId);

        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        using var document = JsonDocument.Parse(await closeResponse.Content.ReadAsStringAsync());
        // consolidatedScores is organizer-only (contracts/rest-api.md, signalr-hub.md): the closing
        // judge's own HTTP response must not carry per-sample means, only confirmation. The mean
        // computation itself (CloseTableRules.ComputeMean) is covered directly by
        // CloseTableTests.cs's unit tests and, end-to-end via a real persisted evaluation, by
        // Organizer_correction_after_close_succeeds_recomputes_total_and_mean_and_is_audited below.
        Assert.Equal(fixture.TableId, document.RootElement.GetProperty("tableId").GetGuid());
        Assert.False(document.RootElement.TryGetProperty("consolidatedScores", out _));

        var (state, closedAt) = await GetTableStateAsync(fixture.TableId);
        Assert.Equal(TableState.Closed, state);
        Assert.NotNull(closedAt);
    }

    // ---- Post-close immutability: the existing SubmitEvaluation gate now engages (FR-034) --------

    [Fact]
    public async Task Post_close_evaluation_submission_by_a_judge_seeded_after_close_returns_409_table_closed()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CloseAsync(judgeClient, fixture.TableId)).StatusCode);

        // A second judge, seeded (and assigned to the table) only after close — proves the
        // *existing* SubmitEvaluation table-state gate (T058) is what engages once CloseTable
        // flips TastingTable.State, not merely that direct test seeding of a closed table works.
        var latecomerSub = $"judge-{Guid.NewGuid():N}";
        var latecomerId = await SeedJudgeAsync(fixture.CompetitionId, $"{latecomerSub}@brew.example", latecomerSub);
        await SeedTableJudgeAsync(fixture.TableId, latecomerId);
        using var latecomerClient = JudgeClient(latecomerSub);

        var lateSubmit = await SubmitAsync(latecomerClient, fixture.TableId, fixture.EntryIds[0]);

        Assert.Equal(HttpStatusCode.Conflict, lateSubmit.StatusCode);
        using var document = JsonDocument.Parse(await lateSubmit.Content.ReadAsStringAsync());
        Assert.Contains("table-closed", document.RootElement.GetProperty("type").GetString());
    }

    // ---- Organizer post-close correction (FR-035) --------------------------------------------------

    [Fact]
    public async Task Organizer_correction_after_close_succeeds_recomputes_total_and_mean_and_is_audited()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        var originalScores = new { aroma = 10, appearance = 2, flavor = 15, mouthfeel = 4, overall = 8 }; // total 39
        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0], originalScores);
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evaluationId").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await CloseAsync(judgeClient, fixture.TableId)).StatusCode);

        var correctedScores = new { aroma = 12, appearance = 3, flavor = 20, mouthfeel = 5, overall = 10 }; // total 50
        var correctionResponse = await CorrectAsync(organizer, fixture.CompetitionId, evaluationId, correctedScores);

        Assert.Equal(HttpStatusCode.OK, correctionResponse.StatusCode);
        using var document = JsonDocument.Parse(await correctionResponse.Content.ReadAsStringAsync());
        Assert.Equal(50, document.RootElement.GetProperty("total").GetInt32());
        // Only evaluation for this sample, so the consolidated mean equals the corrected total.
        Assert.Equal(50m, document.RootElement.GetProperty("consolidatedMean").GetDecimal());

        var auditLog = await GetAuditLogAsync(evaluationId);
        Assert.Equal("EvaluationCorrected", auditLog.Action);
        Assert.Equal(nameof(Evaluation), auditLog.EntityType);
        Assert.Equal(evaluationId.ToString(), auditLog.EntityId);

        using var data = JsonDocument.Parse(auditLog.DataJson);
        Assert.Equal(8, data.RootElement.GetProperty("before").GetProperty("OverallScore").GetInt32());
        Assert.Equal(10, data.RootElement.GetProperty("after").GetProperty("OverallScore").GetInt32());
    }

    [Fact]
    public async Task Correction_by_a_non_owning_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(owner);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0]);
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evaluationId").GetGuid();
        await CloseAsync(judgeClient, fixture.TableId);

        using var otherOrganizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await CorrectAsync(otherOrganizer, fixture.CompetitionId, evaluationId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Correction_of_a_non_existent_evaluation_returns_404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);

        var response = await CorrectAsync(organizer, fixture.CompetitionId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Boundary validation reuses SubmitEvaluation's rules (400s) -------------------------------

    [Fact]
    public async Task Correction_with_an_out_of_range_score_returns_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);
        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0]);
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evaluationId").GetGuid();

        var invalidScores = new { aroma = 13, appearance = 2, flavor = 15, mouthfeel = 4, overall = 8 }; // aroma cap is 12

        var response = await CorrectAsync(organizer, fixture.CompetitionId, evaluationId, invalidScores);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Correction_with_a_comment_under_20_characters_returns_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);
        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0]);
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evaluationId").GetGuid();

        var shortComments = new
        {
            aroma = "too short",
            appearance = LongComment,
            flavor = LongComment,
            mouthfeel = LongComment,
            overall = LongComment,
        };

        var response = await CorrectAsync(organizer, fixture.CompetitionId, evaluationId, ValidScores(), shortComments);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
