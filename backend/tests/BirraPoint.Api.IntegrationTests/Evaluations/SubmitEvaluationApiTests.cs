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
/// T056: HTTP-level contract tests for POST /me/tables/{tableId}/evaluations (contracts/rest-api.md
/// §Judge workspace) — the full FR-022 precondition chain (state/order-fixed/table-closed/
/// out-of-sequence → 409), FR-023/FR-025 boundary validation (→ 400), the FR-029/R-07 idempotent
/// replay (→ 200 with the stored result), and the concurrency race the unique index backstops.
/// </summary>
public sealed class SubmitEvaluationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
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

    /// <summary>Same email convention as OrderApiTests — GetJudgeRecordsAsync resolves Judge rows
    /// by email, not `sub`.</summary>
    private HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, $"{sub}@brew.example", "JUDGE"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Eval")
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

    /// <summary>Directly assigns SequenceOrder (1..M, per <paramref name="orderedEntryIds"/>) and
    /// sets OrderFixedByJudgeId/OrderFixedAt — mirrors what POST .../order (FixOrder, T050) would
    /// do, without needing the extra HTTP round-trip for tests that only care about downstream
    /// evaluation preconditions.</summary>
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

    private async Task CloseTableDirectlyAsync(Guid tableId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = await db.TastingTables.SingleAsync(t => t.Id == tableId);
        table.State = TableState.Closed;
        table.ClosedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task<int> CountEvaluationsAsync(Guid judgeId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Evaluations.CountAsync(e => e.JudgeId == judgeId && e.BeerEntryId == beerEntryId);
    }

    /// <summary>Seeds a competition, one table with <paramref name="sampleCount"/> samples, and one
    /// actively-assigned judge — the common fixture below. Competition state and order-fixed-ness
    /// are left to the caller so every precondition combination can be exercised.</summary>
    private async Task<(Guid CompetitionId, Guid TableId, Guid JudgeId, string JudgeSub, List<Guid> EntryIds)>
        SeedTableWithSamplesAsync(HttpClient organizer, int sampleCount = 3)
    {
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedTableAsync(competitionId, $"Table {Guid.NewGuid():N}");

        var judgeSub = $"judge-{Guid.NewGuid():N}";
        var judgeEmail = $"{judgeSub}@brew.example";
        var judgeId = await SeedJudgeAsync(competitionId, judgeEmail, judgeSub);
        await SeedTableJudgeAsync(tableId, judgeId);

        var entryIds = new List<Guid>();
        for (var i = 0; i < sampleCount; i++)
        {
            var participantId = await SeedParticipantAsync(
                competitionId, $"Brewer {i}", $"brewer-{Guid.NewGuid():N}@brew.example");
            var entryId = await SeedBeerEntryAsync(competitionId, participantId, $"Secret Beer {i}");
            await SeedTableSampleAsync(tableId, entryId);
            entryIds.Add(entryId);
        }

        return (competitionId, tableId, judgeId, judgeSub, entryIds);
    }

    /// <summary>Fixture fully ready for a successful submission: competition InEvaluation, order
    /// fixed by the assigned judge.</summary>
    private async Task<(Guid CompetitionId, Guid TableId, Guid JudgeId, string JudgeSub, List<Guid> EntryIds)>
        SeedReadyTableAsync(HttpClient organizer, int sampleCount = 3)
    {
        var fixture = await SeedTableWithSamplesAsync(organizer, sampleCount);
        await TransitionStateAsync(organizer, fixture.CompetitionId, "Active");
        await TransitionStateAsync(organizer, fixture.CompetitionId, "InEvaluation");
        await FixOrderDirectlyAsync(fixture.TableId, fixture.JudgeId, fixture.EntryIds);
        return fixture;
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
        HttpClient client, Guid tableId, Guid beerEntryId, object? scores = null, object? comments = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/me/tables/{tableId}/evaluations")
        {
            Content = JsonContent.Create(new { beerEntryId, scores = scores ?? ValidScores(), comments = comments ?? ValidComments() }),
        };
        if (idempotencyKey is not "OMIT")
        {
            request.Headers.Add("X-Idempotency-Key", idempotencyKey ?? $"comp:{tableId}:judge:{beerEntryId}");
        }

        return client.SendAsync(request);
    }

    // ---- Precondition chain (409s) ---------------------------------------------------------------

    [Fact]
    public async Task Submit_while_competition_is_not_InEvaluation_returns_409_invalid_state_transition()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedTableWithSamplesAsync(organizer);
        await TransitionStateAsync(organizer, fixture.CompetitionId, "Active"); // stays Active, never InEvaluation

        using var judge = JudgeClient(fixture.JudgeSub);
        var response = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Submit_before_the_order_is_fixed_returns_409_order_not_fixed()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedTableWithSamplesAsync(organizer);
        await TransitionStateAsync(organizer, fixture.CompetitionId, "Active");
        await TransitionStateAsync(organizer, fixture.CompetitionId, "InEvaluation");
        // Order never fixed.

        using var judge = JudgeClient(fixture.JudgeSub);
        var response = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("order-not-fixed", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Submit_against_a_closed_table_returns_409_table_closed()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        await CloseTableDirectlyAsync(fixture.TableId);

        using var judge = JudgeClient(fixture.JudgeSub);
        var response = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("table-closed", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Submit_a_sample_out_of_sequence_returns_409_out_of_sequence()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, sampleCount: 3);

        using var judge = JudgeClient(fixture.JudgeSub);
        var first = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0]);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Skips EntryIds[1] — the true next sample.
        var response = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[2]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("out-of-sequence", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Submit_for_a_table_the_judge_is_not_a_member_of_returns_404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);

        using var outsider = JudgeClient($"outsider-{Guid.NewGuid():N}");
        var response = await SubmitAsync(outsider, fixture.TableId, fixture.EntryIds[0]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Boundary validation (400s, FR-023/FR-025) -----------------------------------------------

    [Fact]
    public async Task Submit_with_an_out_of_range_score_returns_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);

        using var judge = JudgeClient(fixture.JudgeSub);
        var scores = new { aroma = 13, appearance = 2, flavor = 15, mouthfeel = 4, overall = 8 }; // aroma cap is 12
        var response = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0], scores: scores);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_with_a_comment_under_20_characters_returns_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);

        using var judge = JudgeClient(fixture.JudgeSub);
        var comments = new { aroma = "too short", appearance = LongComment, flavor = LongComment, mouthfeel = LongComment, overall = LongComment };
        var response = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0], comments: comments);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_without_the_idempotency_key_header_returns_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);

        using var judge = JudgeClient(fixture.JudgeSub);
        var response = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0], idempotencyKey: "OMIT");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Happy path -------------------------------------------------------------------------------

    [Fact]
    public async Task Submit_the_first_sample_in_order_returns_201_with_the_computed_total()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);

        using var judge = JudgeClient(fixture.JudgeSub);
        var response = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.String, root.GetProperty("evaluationId").ValueKind);
        Assert.Equal("Confirmed", root.GetProperty("status").GetString());
        Assert.Equal(10 + 2 + 15 + 4 + 8, root.GetProperty("total").GetInt32());
    }

    // ---- Idempotent replay (FR-029/R-07) -----------------------------------------------------------

    [Fact]
    public async Task Submitting_the_same_judge_entry_pair_twice_replays_the_stored_result()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);

        using var judge = JudgeClient(fixture.JudgeSub);
        var first = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0]);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstEvaluationId = firstDocument.RootElement.GetProperty("evaluationId").GetGuid();
        var firstTotal = firstDocument.RootElement.GetProperty("total").GetInt32();

        var second = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0]);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondDocument = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(firstEvaluationId, secondDocument.RootElement.GetProperty("evaluationId").GetGuid());
        Assert.Equal(firstTotal, secondDocument.RootElement.GetProperty("total").GetInt32());

        Assert.Equal(1, await CountEvaluationsAsync(fixture.JudgeId, fixture.EntryIds[0]));
    }

    [Fact]
    public async Task Replaying_an_already_stored_evaluation_after_the_table_closes_still_returns_200_not_409()
    {
        // FR-029/R-07: idempotency must hold regardless of what happens to the table afterward — a
        // judge's evaluation is a fact once persisted. This reproduces the exact scenario the
        // offline outbox's replay engine exists for: the original submission succeeded and
        // committed, but the client never saw the ack (dropped connection) and later replays it —
        // by which point the table has since closed. Must still be 200 with the stored result, not
        // 409 table-closed (senior-code-reviewer finding on PR #22).
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);

        using var judge = JudgeClient(fixture.JudgeSub);
        var first = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0]);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstEvaluationId = firstDocument.RootElement.GetProperty("evaluationId").GetGuid();

        await CloseTableDirectlyAsync(fixture.TableId);

        var replay = await SubmitAsync(judge, fixture.TableId, fixture.EntryIds[0]);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayDocument = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(firstEvaluationId, replayDocument.RootElement.GetProperty("evaluationId").GetGuid());
        Assert.Equal(1, await CountEvaluationsAsync(fixture.JudgeId, fixture.EntryIds[0]));
    }

    // ---- Concurrency race (proves the unique-constraint catch path, not just sequential replay) --

    [Fact]
    public async Task Two_near_simultaneous_submissions_for_the_same_pair_leave_exactly_one_row()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);

        using var judgeA = JudgeClient(fixture.JudgeSub);
        using var judgeB = JudgeClient(fixture.JudgeSub);

        var taskA = SubmitAsync(judgeA, fixture.TableId, fixture.EntryIds[0]);
        var taskB = SubmitAsync(judgeB, fixture.TableId, fixture.EntryIds[0]);
        var responses = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        Assert.Equal(1, await CountEvaluationsAsync(fixture.JudgeId, fixture.EntryIds[0]));
    }
}
