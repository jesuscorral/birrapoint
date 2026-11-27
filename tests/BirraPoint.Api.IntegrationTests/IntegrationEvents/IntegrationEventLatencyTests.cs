using System.Diagnostics;
using System.Net;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>
/// The dispatcher is woken right after commit, so the reaction does not wait for the periodic poll
/// (FR-089, SC-020): with the safety-net poll at one hour, 95% of table-judge changes show their Best of Show
/// flag within one second of the originating commit.
/// </summary>
public sealed class IntegrationEventLatencyTests(ApiFactory factory) : IntegrationEventsTestBase(factory), IClassFixture<ApiFactory>
{
    private const int Changes = 20;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task TableJudgeChangesShowTheirBosFlagWithinOneSecondAtP95WithTheSafetyNetPollAtOneHour()
    {
        await using var host = NewHost(safetyNetPoll: "01:00:00");
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var services = host.Services;
        var competitionId = await CreateCompetitionAsync(organizer);

        var sampleParticipantId = await SeedParticipantAsync(services, competitionId, "Sample Brewer", NewEmail("sample"));
        var sampleEntryId = await SeedEntryAsync(services, competitionId, sampleParticipantId, "Sample Brew");
        var bystanderJudgeId = await SeedJudgeAsync(services, competitionId, NewEmail("bystander"));
        var tableName = $"Table {Guid.NewGuid():N}";
        var tableId = await ReadIdAsync(await CreateTableAsync(organizer, competitionId, tableName, [bystanderJudgeId], [sampleEntryId]));

        // One judge per change, each owning an entry that the change must flag.
        var changes = new List<(Guid JudgeId, Guid EntryId)>();
        for (var i = 0; i < Changes + 1; i++)
        {
            var email = NewEmail($"judge{i}");
            var participantId = await SeedParticipantAsync(services, competitionId, $"Owner {i}", email);
            changes.Add((await SeedJudgeAsync(services, competitionId, email), await SeedEntryAsync(services, competitionId, participantId, $"Owned {i}")));
        }

        // The first change is a warm-up (JIT, connection pool, first dispatcher sweep): it is not measured.
        var latencies = new List<TimeSpan>();
        for (var i = 0; i < changes.Count; i++)
        {
            var (judgeId, entryId) = changes[i];
            var response = await UpdateTableAsync(organizer, competitionId, tableId, tableName, [bystanderJudgeId, judgeId], [sampleEntryId]);
            var committed = Stopwatch.StartNew(); // the response is sent after the commit
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            while (!await IsNotValidForBosAsync(services, entryId))
            {
                Assert.True(committed.Elapsed < EventualAssert.DefaultTimeout, $"Flag of change {i} not visible after {committed.Elapsed}.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            if (i > 0)
            {
                latencies.Add(committed.Elapsed);
            }
        }

        var p95 = latencies.Order().ElementAt((int)Math.Ceiling(0.95 * latencies.Count) - 1);
        Assert.True(p95 <= Budget, $"p95 commit-to-flag was {p95}, budget {Budget}.");

        // Every change travelled as an event (not a same-transaction write) and was delivered.
        var events = await MessagingDb.OutboxAsync(ConnectionString, MessagingDb.Judging, MessagingDb.TableJudgesChanged, competitionId);
        Assert.True(events.Count >= Changes, $"Expected at least {Changes} events, found {events.Count}.");
        await EventualAssert.TrueAsync(
            async () => (await MessagingDb.OutboxAsync(ConnectionString, MessagingDb.Judging, MessagingDb.TableJudgesChanged, competitionId))
                .All(e => e.Status == "Delivered"),
            "every table-judges-changed event delivered");
    }
}
