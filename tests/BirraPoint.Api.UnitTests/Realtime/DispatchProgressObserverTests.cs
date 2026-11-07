using System.Text.Json;
using System.Text.Json.Serialization;
using BirraPoint.Api.Realtime;
using BuildingBlocks.Jobs;

namespace BirraPoint.Api.UnitTests.Realtime;

/// <summary>The `DispatchProgress` wire shape (contracts/signalr-hub.md) survives the move of the job queue into a
/// building block: same audience, same event name, same camelCase JSON with enum names.</summary>
public sealed class DispatchProgressObserverTests
{
    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<(Guid CompetitionId, string EventName, object Payload)> Organizers { get; } = [];

        public Task PublishToOrganizersAsync(
            Guid competitionId, string eventName, object payload, CancellationToken cancellationToken = default)
        {
            Organizers.Add((competitionId, eventName, payload));
            return Task.CompletedTask;
        }

        public Task PublishToTableAsync(
            Guid tableId, string eventName, object payload, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static readonly JsonSerializerOptions WireOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task PublishesTheJobsStateToTheOrganizersOfItsCompetition()
    {
        var publisher = new RecordingPublisher();
        var competitionId = Guid.NewGuid();
        var job = new Job { GroupKey = competitionId, Type = "SendResultEmail", PayloadJson = "{}", Status = JobStatus.Failed };

        await new DispatchProgressObserver(publisher).OnStateChangedAsync(job, "SMTP down", TestContext.Current.CancellationToken);

        var sent = Assert.Single(publisher.Organizers);
        Assert.Equal(competitionId, sent.CompetitionId);
        Assert.Equal(CompetitionEvents.DispatchProgress, sent.EventName);
        Assert.Equal(new DispatchProgressPayload("SendResultEmail", JobStatus.Failed, "SMTP down"), sent.Payload);
    }

    [Fact]
    public void PayloadSerializesJobTypeAndStatusAsNames()
    {
        var json = JsonSerializer.Serialize(new DispatchProgressPayload("BundleZip", JobStatus.Completed, null), WireOptions);

        Assert.Equal("""{"jobType":"BundleZip","status":"Completed","detail":null}""", json);
    }
}
