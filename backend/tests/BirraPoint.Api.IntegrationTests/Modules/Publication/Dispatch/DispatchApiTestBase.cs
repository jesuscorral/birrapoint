using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Publication.Dispatch;

/// <summary>Shared clients, seeding and polling helpers for the Dispatch slice contract tests
/// (contracts/rest-api.md §Results &amp; dispatch).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "Base class of public xUnit test classes; C# requires a base class to be at least as accessible as its derived classes.")]
public abstract class DispatchApiTestBase(ApiFactory factory)
{
    private protected const string StyleCodeApa = "21A";
    private const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

    private protected static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private protected static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private protected ApiFactory Factory => factory;

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
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, $"{sub}@brew.example", "JUDGE"));
        return client;
    }

    private protected FakeEmailSender FakeEmailSender => (FakeEmailSender)factory.Services.GetRequiredService<IEmailSender>();

    private protected static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Dispatch") =>
        (await CreateCompetitionWithNameAsync(client, namePrefix)).Id;

    private protected static async Task<(Guid Id, string Name)> CreateCompetitionWithNameAsync(HttpClient client, string namePrefix)
    {
        var name = $"{namePrefix} {Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name,
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (created.GetProperty("id").GetGuid(), name);
    }

    private protected static string NewBlindCode() => $"B{Guid.NewGuid():N}"[..8];

    private protected static async Task<HttpResponseMessage> TransitionToActiveAndInEvaluationAsync(HttpClient organizer, Guid competitionId)
    {
        await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Active" });
        return await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "InEvaluation" });
    }

    private protected static Task<HttpResponseMessage> FinalizeAsync(HttpClient organizer, Guid competitionId) =>
        organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Finalized" });

    private protected async Task<Guid> SeedParticipantAsync(Guid competitionId, string name, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var participant = new Participant { CompetitionId = competitionId, Name = name, Email = email };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();
        return participant.Id;
    }

    private protected async Task<Guid> SeedBeerEntryAsync(
        Guid competitionId, Guid participantId, string beerName, string blindCode, string styleCode = StyleCodeApa)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = new BeerEntry
        {
            CompetitionId = competitionId,
            ParticipantId = participantId,
            BeerName = beerName,
            StyleCode = styleCode,
            BlindCode = blindCode,
        };
        db.BeerEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private protected async Task<Guid> SeedJudgeAsync(Guid competitionId, string email, string keycloakUserId)
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

    private protected async Task<Guid> SeedTableAsync(Guid competitionId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = new TastingTable { CompetitionId = competitionId, Name = name };
        db.TastingTables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    private protected async Task SeedTableJudgeAsync(Guid tableId, Guid judgeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableJudges.Add(new TableJudge { TastingTableId = tableId, JudgeId = judgeId });
        await db.SaveChangesAsync();
    }

    private protected async Task SeedTableSampleAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableSamples.Add(new TableSample { TastingTableId = tableId, BeerEntryId = beerEntryId });
        await db.SaveChangesAsync();
    }

    private protected async Task SeedEvaluationAsync(Guid tableId, Guid judgeId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Evaluations.Add(new Evaluation
        {
            TastingTableId = tableId,
            JudgeId = judgeId,
            BeerEntryId = beerEntryId,
            AromaScore = 10,
            AppearanceScore = 2,
            FlavorScore = 15,
            MouthfeelScore = 4,
            OverallScore = 8,
            AromaComment = LongComment,
            AppearanceComment = LongComment,
            FlavorComment = LongComment,
            MouthfeelComment = LongComment,
            OverallComment = LongComment,
            Status = EvaluationStatus.Confirmed,
            SubmittedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private protected async Task<Guid> SeedDispatchJobAsync(
        Guid competitionId, DispatchJobType type, DispatchJobStatus status, object payload,
        int attempts = 0, string? lastError = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new DispatchJob
        {
            CompetitionId = competitionId,
            Type = type,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = status,
            Attempts = attempts,
            LastError = lastError,
        };
        db.DispatchJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private protected static Task<HttpResponseMessage> GetResultsArchiveAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/results/archive"));

    private protected static Task<HttpResponseMessage> GetDispatchStatusAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/dispatch"));

    private protected static Task<HttpResponseMessage> RetryDispatchAsync(HttpClient client, Guid competitionId, params Guid[] participantIds) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/dispatch/retries", new { participantIds });

    private protected static async Task<byte[]> PollForArchiveReadyAsync(HttpClient organizer, Guid competitionId)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await GetResultsArchiveAsync(organizer, competitionId);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await Task.Delay(PollInterval);
        }

        Assert.Fail("Timed out waiting for the results archive to become ready.");
        throw new UnreachableException();
    }

    private protected static async Task<List<JsonElement>> PollForAllDispatchRowsCompletedAsync(
        HttpClient organizer, Guid competitionId, int expectedCount, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? PollTimeout);
        while (DateTime.UtcNow < deadline)
        {
            var response = await GetDispatchStatusAsync(organizer, competitionId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rows = document.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();

            if (rows.Count == expectedCount && rows.All(r => r.GetProperty("status").GetString() == "Completed"))
            {
                return rows;
            }

            await Task.Delay(PollInterval);
        }

        Assert.Fail("Timed out waiting for every dispatch job to reach Completed.");
        throw new UnreachableException();
    }
}
