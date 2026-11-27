using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>Outbox and inbox names route to the context that declared them: the row commits with the publishing
/// context's save, the dispatcher delivers across contexts, the purger cleans every schema (R-107-3).</summary>
public sealed class MultiContextRoutingTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static void Declare(MessagingBuilder m)
    {
        m.Outbox<ATestDbContext>("A");
        m.Inbox<ATestDbContext>("A");
        m.Inbox<BTestDbContext>("B");
        m.Publishes<ThingHappened>("A");
        m.Subscribe<ThingHappened, BNoteHandler>("B");
    }

    private async Task<MessagingHost> StartAsync(FakeTimeProvider? clock = null, bool dispatcherEnabled = true)
    {
        await ResetAsync();
        return await fixture.StartAsync(
            configure: Declare,
            services: s =>
            {
                s.AddDbContext<ATestDbContext>((sp, o) => o.UseNpgsql(fixture.ConnectionString).AddMessagingInterceptors(sp));
                s.AddDbContext<BTestDbContext>((sp, o) => o.UseNpgsql(fixture.ConnectionString).AddMessagingInterceptors(sp));
                if (clock is not null)
                {
                    s.AddSingleton<TimeProvider>(clock);
                }
            },
            dispatcherEnabled: dispatcherEnabled,
            declareStandard: false);
    }

    private async Task ResetAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var a = fixture.NewContext<ATestDbContext>();
        await using var b = fixture.NewContext<BTestDbContext>();
        await a.Set<OutboxMessage>(MessagingNames.Outbox("A")).ExecuteDeleteAsync(ct);
        await a.Set<InboxMessage>(MessagingNames.Inbox("A")).ExecuteDeleteAsync(ct);
        await b.Set<InboxMessage>(MessagingNames.Inbox("B")).ExecuteDeleteAsync(ct);
        await b.Notes.ExecuteDeleteAsync(ct);
    }

    private async Task<List<OutboxMessage>> OutboxAsync()
    {
        await using var a = fixture.NewContext<ATestDbContext>();
        return await a.Set<OutboxMessage>(MessagingNames.Outbox("A")).AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnEventIsStoredInTheDeclaringContextsSchemaAndCommitsWithItsSave()
    {
        await using var host = await StartAsync(dispatcherEnabled: false);
        using var scope = host.Services.CreateScope();
        var ct = TestContext.Current.CancellationToken;

        var id = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().Publish(new ThingHappened(Guid.NewGuid()));

        Assert.Empty(await OutboxAsync());
        await scope.ServiceProvider.GetRequiredService<ATestDbContext>().SaveChangesAsync(ct);
        Assert.Equal(id, Assert.Single(await OutboxAsync()).Id);
        await using var raw = fixture.NewContext<ATestDbContext>();
        Assert.Equal(1, await raw.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM a."OutboxMessages" """).SingleAsync(ct));
    }

    [Fact]
    public async Task TheDispatcherDeliversToAHandlerWhoseInboxLivesInAnotherContext()
    {
        await using var host = await StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var thingId = Guid.NewGuid();

        using (var scope = host.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().Publish(new ThingHappened(thingId));
            await scope.ServiceProvider.GetRequiredService<ATestDbContext>().SaveChangesAsync(ct);
        }

        await Wait.UntilAsync(
            async () => (await OutboxAsync()) is [{ Status: OutboxStatus.Delivered }],
            "event delivered");

        await using var b = fixture.NewContext<BTestDbContext>();
        Assert.Equal(thingId.ToString(), Assert.Single(await b.Notes.AsNoTracking().ToListAsync(ct)).Text);
        var inbox = Assert.Single(await b.Set<InboxMessage>(MessagingNames.Inbox("B")).AsNoTracking().ToListAsync(ct));
        Assert.Equal(nameof(BNoteHandler), inbox.Handler);
        await using var a = fixture.NewContext<ATestDbContext>();
        Assert.Empty(await a.Set<InboxMessage>(MessagingNames.Inbox("A")).AsNoTracking().ToListAsync(ct));
    }

    [Fact]
    public async Task ThePurgerCleansTheOutboxAndInboxesOfEveryContext()
    {
        var clock = new FakeTimeProvider(Start);
        await using var host = await StartAsync(clock, dispatcherEnabled: false);
        var ct = TestContext.Current.CancellationToken;
        await using (var a = fixture.NewContext<ATestDbContext>())
        await using (var b = fixture.NewContext<BTestDbContext>())
        {
            a.Set<OutboxMessage>(MessagingNames.Outbox("A")).Add(new OutboxMessage
            {
                Type = "test.thing-happened",
                Version = 1,
                OccurredAt = Start - TimeSpan.FromDays(40),
                PayloadJson = "{}",
                Status = OutboxStatus.Delivered,
            });
            a.Set<InboxMessage>(MessagingNames.Inbox("A")).Add(new InboxMessage { EventId = Guid.NewGuid(), Handler = "H", ProcessedAt = Start - TimeSpan.FromDays(40) });
            b.Set<InboxMessage>(MessagingNames.Inbox("B")).Add(new InboxMessage { EventId = Guid.NewGuid(), Handler = "H", ProcessedAt = Start - TimeSpan.FromDays(40) });
            await a.SaveChangesAsync(ct);
            await b.SaveChangesAsync(ct);
        }

        Assert.True(await host.Services.GetRequiredService<RetentionPurger>().RunIfDueAsync(ct));

        Assert.Empty(await OutboxAsync());
        await using var a2 = fixture.NewContext<ATestDbContext>();
        await using var b2 = fixture.NewContext<BTestDbContext>();
        Assert.Empty(await a2.Set<InboxMessage>(MessagingNames.Inbox("A")).ToListAsync(ct));
        Assert.Empty(await b2.Set<InboxMessage>(MessagingNames.Inbox("B")).ToListAsync(ct));
    }

    [Fact]
    public void ANameDeclaredTwiceForDifferentContextsIsRejected()
    {
        var builder = new MessagingBuilder();
        builder.Outbox<ATestDbContext>("X");
        builder.Inbox<ATestDbContext>("X");

        Assert.Throws<InvalidOperationException>(() => builder.Outbox<BTestDbContext>("X"));
        Assert.Throws<InvalidOperationException>(() => builder.Inbox<BTestDbContext>("X"));
        Assert.Null(Record.Exception(() => builder.Outbox<ATestDbContext>("X")));
    }

    [Fact]
    public void PublishingOrSubscribingWithAnUndeclaredNameIsRejected()
    {
        var builder = new MessagingBuilder();

        var publish = Assert.Throws<InvalidOperationException>(() => builder.Publishes<ThingHappened>("Nope"));
        var subscribe = Assert.Throws<InvalidOperationException>(() => builder.Subscribe<ThingHappened, BNoteHandler>("Nope"));

        Assert.Contains("Outbox<TContext>", publish.Message, StringComparison.Ordinal);
        Assert.Contains("Inbox<TContext>", subscribe.Message, StringComparison.Ordinal);
    }
}
