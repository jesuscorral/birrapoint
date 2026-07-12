using BirraPoint.Api.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace BirraPoint.Api.UnitTests.Realtime;

public sealed class EventPublisherTests
{
    [Fact]
    public async Task PublishToOrganizersAsync_sends_the_event_to_the_organizer_group()
    {
        var hubContext = new FakeHubContext();
        var publisher = new EventPublisher(hubContext);
        var competitionId = Guid.NewGuid();
        var payload = new { competitionId, state = "Active" };

        await publisher.PublishToOrganizersAsync(competitionId, CompetitionEvents.CompetitionStateChanged, payload);

        var proxy = hubContext.Clients.RequireGroup(CompetitionGroups.Organizers(competitionId));
        var sent = Assert.Single(proxy.Sent);
        Assert.Equal(CompetitionEvents.CompetitionStateChanged, sent.Method);
        Assert.Same(payload, Assert.Single(sent.Args));
    }

    [Fact]
    public async Task PublishToTableAsync_sends_the_event_to_the_table_group_only()
    {
        var hubContext = new FakeHubContext();
        var publisher = new EventPublisher(hubContext);
        var tableId = Guid.NewGuid();
        var payload = new { tableId };

        await publisher.PublishToTableAsync(tableId, CompetitionEvents.TableClosed, payload);

        var proxy = hubContext.Clients.RequireGroup(CompetitionGroups.Table(tableId));
        var sent = Assert.Single(proxy.Sent);
        Assert.Equal(CompetitionEvents.TableClosed, sent.Method);
        Assert.Same(payload, Assert.Single(sent.Args));
        Assert.Equal([CompetitionGroups.Table(tableId)], hubContext.Clients.GroupCalls.Keys);
    }

    private sealed class FakeHubContext : IHubContext<CompetitionHub>
    {
        public FakeHubClients Clients { get; } = new();

        IHubClients IHubContext<CompetitionHub>.Clients => Clients;

        public IGroupManager Groups => throw new NotSupportedException("Not exercised by EventPublisher.");
    }

    private sealed class FakeHubClients : IHubClients
    {
        public Dictionary<string, FakeClientProxy> GroupCalls { get; } = [];

        public FakeClientProxy RequireGroup(string groupName) => GroupCalls[groupName];

        public IClientProxy Group(string groupName)
        {
            if (!GroupCalls.TryGetValue(groupName, out var proxy))
            {
                proxy = new FakeClientProxy();
                GroupCalls[groupName] = proxy;
            }

            return proxy;
        }

        public IClientProxy All => throw new NotSupportedException();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Client(string connectionId) => throw new NotSupportedException();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public IClientProxy OthersInGroup(string groupName) => throw new NotSupportedException();

        public IClientProxy User(string userId) => throw new NotSupportedException();

        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class FakeClientProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> Sent { get; } = [];

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add((method, args));
            return Task.CompletedTask;
        }
    }
}
