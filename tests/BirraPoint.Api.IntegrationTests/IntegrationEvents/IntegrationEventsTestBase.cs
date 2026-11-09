using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>Everything seeded for a competition whose only table is closed with one evaluation per entry.</summary>
internal sealed record ClosedCompetition(
    Guid CompetitionId, string CompetitionName, Guid TableId, Guid JudgeId, string JudgeSub,
    IReadOnlyList<ClosedEntry> Entries);

internal sealed record ClosedEntry(Guid EntryId, Guid ParticipantId, string ParticipantEmail, Guid EvaluationId);

/// <summary>
/// Shared hosts, clients and seeding for the integration-event flow tests. Hosts are always derived from the
/// class factory with the messaging settings of the scenario (<c>Messaging:DispatcherEnabled</c>,
/// <c>Messaging:SafetyNetPollInterval</c>) and are never the factory's own host: that one would keep delivering
/// the events a scenario holds back. They share the factory's database, so the tests inspect rows through
/// <see cref="ConnectionString"/> with plain SQL.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "Base class of public xUnit test classes; C# requires a base class to be at least as accessible as its derived classes.")]
public abstract class IntegrationEventsTestBase(ApiFactory factory)
{
    private protected const string StyleCodeApa = "21A";
    private protected const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

    private protected string ConnectionString => factory.ConnectionString;

    /// <summary>A host derived from the class factory with the messaging settings of the scenario.</summary>
    /// <param name="dispatcherEnabled">false holds every delivery (<c>Messaging:DispatcherEnabled=false</c>).</param>
    /// <param name="safetyNetPoll">Messaging safety-net poll; one hour proves the commit wake-up, one second lets
    /// a forced redelivery be picked up without a commit.</param>
    /// <param name="configureServices">Extra test-only services (for example a database interceptor).</param>
    private protected WebApplicationFactory<Program> NewHost(
        bool dispatcherEnabled = true, string safetyNetPoll = "00:00:01", Action<IServiceCollection>? configureServices = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Messaging:DispatcherEnabled"] = dispatcherEnabled ? "true" : "false",
                    ["Messaging:SafetyNetPollInterval"] = safetyNetPoll,
                }));
            if (configureServices is not null)
            {
                builder.ConfigureServices(configureServices);
            }
        });

    private protected static HttpClient OrganizerClient(WebApplicationFactory<Program> host, string sub)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private protected static HttpClient JudgeClient(WebApplicationFactory<Program> host, string sub)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, $"{sub}@brew.example", "JUDGE"));
        return client;
    }

    private protected static string NewOrganizerSub() => $"organizer-{Guid.NewGuid():N}";

    private protected static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@brew.example";

    private protected static async Task<Guid> CreateCompetitionAsync(HttpClient organizer, string namePrefix = "Events") =>
        (await CreateCompetitionWithNameAsync(organizer, namePrefix)).Id;

    private protected static async Task<(Guid Id, string Name)> CreateCompetitionWithNameAsync(HttpClient organizer, string namePrefix)
    {
        var name = $"{namePrefix} {Guid.NewGuid():N}";
        var response = await organizer.PostAsJsonAsync("/api/v1/competitions", new
        {
            name,
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return (created.GetProperty("id").GetGuid(), name);
    }

    private protected static Task<HttpResponseMessage> TransitionAsync(HttpClient organizer, Guid competitionId, string target) =>
        organizer.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/state", new { target }, TestContext.Current.CancellationToken);

    private protected static Task<HttpResponseMessage> CreateTableAsync(
        HttpClient organizer, Guid competitionId, string name, IEnumerable<Guid> judgeIds, IEnumerable<Guid> beerEntryIds) =>
        organizer.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/tables", new { name, judgeIds, beerEntryIds }, TestContext.Current.CancellationToken);

    private protected static Task<HttpResponseMessage> UpdateTableAsync(
        HttpClient organizer, Guid competitionId, Guid tableId, string name, IEnumerable<Guid> judgeIds, IEnumerable<Guid> beerEntryIds) =>
        organizer.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/tables/{tableId}", new { name, judgeIds, beerEntryIds },
            TestContext.Current.CancellationToken);

    private protected static Task<HttpResponseMessage> UpdateJudgeEmailAsync(
        HttpClient organizer, Guid competitionId, Guid judgeId, string email) =>
        organizer.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/judges/{judgeId}", new { email }, TestContext.Current.CancellationToken);

    private protected static Task<HttpResponseMessage> CorrectEvaluationAsync(
        HttpClient organizer, Guid competitionId, Guid evaluationId)
    {
        var comments = new
        {
            aroma = LongComment,
            appearance = LongComment,
            flavor = LongComment,
            mouthfeel = LongComment,
            overall = LongComment,
        };
        return organizer.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/evaluations/{evaluationId}",
            new { scores = new { aroma = 12, appearance = 3, flavor = 20, mouthfeel = 5, overall = 10 }, comments },
            TestContext.Current.CancellationToken);
    }

    private protected static async Task<Guid> ReadIdAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"Unexpected {(int)response.StatusCode}.");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    // ---- Direct seeding (through the host's own DbContext) ----------------------------------------

    private static async Task<T> WithDbAsync<T>(IServiceProvider services, Func<AppDbContext, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private protected static Task<Guid> SeedParticipantAsync(IServiceProvider services, Guid competitionId, string name, string email) =>
        WithDbAsync(services, async db =>
        {
            var participant = new Participant { CompetitionId = competitionId, Name = name, Email = email };
            db.Participants.Add(participant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return participant.Id;
        });

    private protected static Task<Guid> SeedEntryAsync(
        IServiceProvider services, Guid competitionId, Guid participantId, string beerName,
        IEnumerable<string>? collaboratorEmails = null) =>
        WithDbAsync(services, async db =>
        {
            var entry = new BeerEntry
            {
                CompetitionId = competitionId,
                ParticipantId = participantId,
                BeerName = beerName,
                StyleCode = StyleCodeApa,
                BlindCode = $"B{Guid.NewGuid():N}"[..8],
            };
            db.BeerEntries.Add(entry);
            foreach (var email in collaboratorEmails ?? [])
            {
                db.EntryCollaborators.Add(new EntryCollaborator { BeerEntryId = entry.Id, Email = email });
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return entry.Id;
        });

    private protected static Task<Guid> SeedJudgeAsync(
        IServiceProvider services, Guid competitionId, string email, string? keycloakUserId = null) =>
        WithDbAsync(services, async db =>
        {
            var judge = new Judge
            {
                CompetitionId = competitionId,
                Email = email,
                DisplayName = email.Split('@')[0],
                KeycloakUserId = keycloakUserId,
            };
            db.Judges.Add(judge);
            db.Invitations.Add(new Invitation { JudgeId = judge.Id });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return judge.Id;
        });

    private protected static Task<Guid> SeedTableAsync(IServiceProvider services, Guid competitionId, string name) =>
        WithDbAsync(services, async db =>
        {
            var table = new TastingTable { CompetitionId = competitionId, Name = name };
            db.TastingTables.Add(table);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return table.Id;
        });

    private protected static Task<bool> SeedTableSeatAsync(IServiceProvider services, Guid tableId, Guid judgeId) =>
        WithDbAsync(services, async db =>
        {
            db.TableJudges.Add(new TableJudge { TastingTableId = tableId, JudgeId = judgeId });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return true;
        });

    private protected static Task<bool> SeedTableSampleAsync(IServiceProvider services, Guid tableId, Guid beerEntryId) =>
        WithDbAsync(services, async db =>
        {
            db.TableSamples.Add(new TableSample { TastingTableId = tableId, BeerEntryId = beerEntryId });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return true;
        });

    private protected static Task<Guid> SeedEvaluationAsync(
        IServiceProvider services, Guid tableId, Guid judgeId, Guid beerEntryId) =>
        WithDbAsync(services, async db =>
        {
            var evaluation = new Evaluation
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
            };
            db.Evaluations.Add(evaluation);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            return evaluation.Id;
        });

    private protected static Task<bool> SeedCompetitionStateAsync(IServiceProvider services, Guid competitionId, CompetitionState state) =>
        WithDbAsync(services, async db =>
        {
            await db.Competitions.Where(c => c.Id == competitionId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.State, state), TestContext.Current.CancellationToken);
            return true;
        });

    private protected static Task<bool> IsNotValidForBosAsync(IServiceProvider services, Guid entryId) =>
        WithDbAsync(services, db => db.BeerEntries.AsNoTracking()
            .Where(e => e.Id == entryId).Select(e => e.NotValidForBos).SingleAsync(TestContext.Current.CancellationToken));

    private protected static Task<bool> IsSeatedAsync(IServiceProvider services, Guid tableId, Guid judgeId) =>
        WithDbAsync(services, db => db.TableJudges.AsNoTracking()
            .AnyAsync(tj => tj.TastingTableId == tableId && tj.JudgeId == judgeId && tj.RemovedAt == null, TestContext.Current.CancellationToken));

    /// <summary>One table with one judge, <paramref name="entryCount"/> entries each evaluated once, the table
    /// closed by that judge and the competition left in <c>InEvaluation</c> (ready to finalize).</summary>
    private protected static async Task<ClosedCompetition> SeedClosedCompetitionAsync(
        WebApplicationFactory<Program> host, HttpClient organizer, string namePrefix, int entryCount = 1)
    {
        var (competitionId, name) = await CreateCompetitionWithNameAsync(organizer, namePrefix);
        var services = host.Services;
        var tableId = await SeedTableAsync(services, competitionId, "Table A");
        var judgeSub = $"judge-{Guid.NewGuid():N}";
        var judgeId = await SeedJudgeAsync(services, competitionId, $"{judgeSub}@brew.example", judgeSub);
        await SeedTableSeatAsync(services, tableId, judgeId);

        var entries = new List<ClosedEntry>();
        for (var i = 0; i < entryCount; i++)
        {
            var email = NewEmail("brewer");
            var participantId = await SeedParticipantAsync(services, competitionId, $"Brewer {i}", email);
            var entryId = await SeedEntryAsync(services, competitionId, participantId, $"Beer {i}");
            await SeedTableSampleAsync(services, tableId, entryId);
            var evaluationId = await SeedEvaluationAsync(services, tableId, judgeId, entryId);
            entries.Add(new ClosedEntry(entryId, participantId, email, evaluationId));
        }

        using var judgeClient = JudgeClient(host, judgeSub);
        var close = await judgeClient.PostAsync(
            Urls.Rel($"/api/v1/me/tables/{tableId}/close"), content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await TransitionAsync(organizer, competitionId, "Active")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TransitionAsync(organizer, competitionId, "InEvaluation")).StatusCode);
        return new ClosedCompetition(competitionId, name, tableId, judgeId, judgeSub, entries);
    }
}
