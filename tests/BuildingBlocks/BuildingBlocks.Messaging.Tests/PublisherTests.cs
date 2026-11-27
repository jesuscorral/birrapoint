using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>Publishing stages an outbox row on the caller's context; it lives and dies with the caller's
/// transaction (FR-077). The dispatcher is off, so only the publish path is exercised.</summary>
public sealed class PublisherTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task PublishOnlyTracksARowUntilTheCallerSaves()
    {
        await fixture.ResetAsync();
        await using var host = await fixture.StartAsync(dispatcherEnabled: false);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDb>();
        var thingId = Guid.NewGuid();

        var id = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().Publish(new ThingHappened(thingId));

        Assert.Empty(await fixture.OutboxRowsAsync("Orders"));
        Assert.Equal(EntityState.Added, db.ChangeTracker.Entries<OutboxMessage>().Single().State);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var row = Assert.Single(await fixture.OutboxRowsAsync("Orders"));
        Assert.Equal(id, row.Id);
        Assert.Equal("test.thing-happened", row.Type);
        Assert.Equal(1, row.Version);
        Assert.Equal(OutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Contains(thingId.ToString(), row.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARolledBackTransactionLeavesNoRowAndACommittedOneKeepsIt()
    {
        await fixture.ResetAsync();
        await using var host = await fixture.StartAsync(dispatcherEnabled: false);
        var ct = TestContext.Current.CancellationToken;

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDb>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().Publish(new ThingHappened(Guid.NewGuid()));
            await db.SaveChangesAsync(ct);
            await tx.RollbackAsync(ct);
        }

        Assert.Empty(await fixture.OutboxRowsAsync("Orders"));

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDb>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().Publish(new ThingHappened(Guid.NewGuid()));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        Assert.Single(await fixture.OutboxRowsAsync("Orders"));
    }

    [Fact]
    public async Task AnEventGoesToTheOutboxRegisteredForItsType()
    {
        await fixture.ResetAsync();
        await using var host = await fixture.StartAsync(dispatcherEnabled: false);

        await host.PublishAsync(new ThingHappened(Guid.NewGuid()));
        await host.PublishAsync(new OtherHappened("note"));

        var orders = Assert.Single(await fixture.OutboxRowsAsync("Orders"));
        var billing = Assert.Single(await fixture.OutboxRowsAsync("Billing"));
        Assert.Equal("test.thing-happened", orders.Type);
        Assert.Equal("test.other-happened", billing.Type);
    }

    [Fact]
    public async Task PublishingAnUnregisteredTypeThrowsAndStagesNothing()
    {
        await fixture.ResetAsync();
        await using var host = await fixture.StartAsync(m => m.Publishes<OtherHappened>("Billing"), dispatcherEnabled: false);
        using var scope = host.Services.CreateScope();

        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        Assert.Throws<InvalidOperationException>(() => publisher.Publish(new ThingHappened(Guid.NewGuid())));
        Assert.Empty(scope.ServiceProvider.GetRequiredService<TestDb>().ChangeTracker.Entries<OutboxMessage>());
    }
}
