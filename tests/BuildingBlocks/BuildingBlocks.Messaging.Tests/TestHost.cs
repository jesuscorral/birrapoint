using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;

#pragma warning disable CA1812 // Handlers are created by the service provider.

namespace BuildingBlocks.Messaging.Tests;

[IntegrationEvent("test.thing-happened", 1)]
internal sealed record ThingHappened(Guid ThingId);

[IntegrationEvent("test.other-happened", 1)]
internal sealed record OtherHappened(string Note);

/// <summary>A host entity the handlers write, so tests can prove an effect commits (or rolls back) with the
/// inbox row.</summary>
internal sealed class Thing
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>Two outboxes (orders, billing) and two inboxes (orders, shipping) on one model, each in its own
/// schema, like the host with several modules.</summary>
internal sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
{
    public DbSet<Thing> Things => Set<Thing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Thing>().ToTable("Things", "work");
        modelBuilder.ApplyOutbox("Orders", "orders");
        modelBuilder.ApplyOutbox("Billing", "billing");
        modelBuilder.ApplyInbox("Orders", "orders");
        modelBuilder.ApplyInbox("Shipping", "shipping");
    }
}

/// <summary>A note a module-B handler writes in schema <c>b</c>.</summary>
internal sealed class BNote
{
    public Guid Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>Module A: outbox and inbox <c>A</c> in schema <c>a</c>. Shares nothing with <see cref="BTestDbContext"/>
/// but the database, like two module contexts of the host (R-107-3).</summary>
internal sealed class ATestDbContext(DbContextOptions<ATestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyOutbox("A", "a");
        modelBuilder.ApplyInbox("A", "a");
    }
}

/// <summary>Module B: inbox <c>B</c> in schema <c>b</c> plus the notes its handler writes.</summary>
internal sealed class BTestDbContext(DbContextOptions<BTestDbContext> options) : DbContext(options)
{
    public DbSet<BNote> Notes => Set<BNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BNote>().ToTable("Notes", "b");
        modelBuilder.ApplyInbox("B", "b");
    }
}

/// <summary>Handles <see cref="ThingHappened"/> on module B's context.</summary>
internal sealed class BNoteHandler(BTestDbContext db) : IIntegrationEventHandler<ThingHappened>
{
    public async Task HandleAsync(ThingHappened evt, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        db.Notes.Add(new BNote { Id = Guid.NewGuid(), Text = evt.ThingId.ToString() });
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Coordinates handlers with the test: counts calls and lets a test inject a delay or a failure.</summary>
internal sealed class Probe
{
    private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

    public ConcurrentQueue<(string Handler, Guid EventId)> Committed { get; } = new();

    /// <summary>Runs inside the handler after its effect was saved; throw to fail the delivery.</summary>
    public Func<string, IntegrationEventContext, CancellationToken, Task>? OnHandle { get; set; }

    /// <summary>Runs from the after-commit callback.</summary>
    public Func<string, Guid, Task>? OnCommitted { get; set; }

    public int CallsOf(string handler) => _calls.GetValueOrDefault(handler);

    public async Task EnterAsync(string handler, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        _calls.AddOrUpdate(handler, 1, (_, n) => n + 1);
        if (OnHandle is { } onHandle)
        {
            await onHandle(handler, context, cancellationToken);
        }
    }

    public async Task CommittedAsync(string handler, Guid eventId)
    {
        if (OnCommitted is { } onCommitted)
        {
            await onCommitted(handler, eventId);
        }

        Committed.Enqueue((handler, eventId));
    }
}

internal static class Handlers
{
    public static async Task RunAsync(
        string handler, TestDb db, Probe probe, Guid thingId, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        db.Things.Add(new Thing { Id = Guid.NewGuid(), Name = ThingName(handler, thingId) });
        await db.SaveChangesAsync(cancellationToken);
        context.OnCommitted(_ => probe.CommittedAsync(handler, context.EventId));
        await probe.EnterAsync(handler, context, cancellationToken);
    }

    public static string ThingName(string handler, Guid thingId) => $"{handler}:{thingId}";
}

internal sealed class FirstHandler(TestDb db, Probe probe) : IIntegrationEventHandler<ThingHappened>
{
    public Task HandleAsync(ThingHappened evt, IntegrationEventContext context, CancellationToken cancellationToken) =>
        Handlers.RunAsync(nameof(FirstHandler), db, probe, evt.ThingId, context, cancellationToken);
}

internal sealed class SecondHandler(TestDb db, Probe probe) : IIntegrationEventHandler<ThingHappened>
{
    public Task HandleAsync(ThingHappened evt, IntegrationEventContext context, CancellationToken cancellationToken) =>
        Handlers.RunAsync(nameof(SecondHandler), db, probe, evt.ThingId, context, cancellationToken);
}

internal sealed class OtherHandler(Probe probe) : IIntegrationEventHandler<OtherHappened>
{
    public Task HandleAsync(OtherHappened evt, IntegrationEventContext context, CancellationToken cancellationToken) =>
        probe.EnterAsync(nameof(OtherHandler), context, cancellationToken);
}

/// <summary>Keeps every log entry so tests can assert on levels and on what was (not) written.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<(string, LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((category, logLevel, formatter(state, exception)));
    }
}

/// <summary>A clock tests move by hand.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public void Advance(TimeSpan by) => _now += by;

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>One PostgreSQL 16 container per test class; schema created from the test model.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    internal string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewDb();
        await db.Database.EnsureCreatedAsync();

        // The other contexts share this database; EnsureCreated skips it once tables exist, so create theirs directly.
        await using var a = NewContext<ATestDbContext>();
        await using var b = NewContext<BTestDbContext>();
        foreach (var other in new DbContext[] { a, b })
        {
            await other.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }
    }

    ValueTask IAsyncDisposable.DisposeAsync() => _container.DisposeAsync();

    internal TContext NewContext<TContext>()
        where TContext : DbContext =>
        (TContext)Activator.CreateInstance(typeof(TContext), new DbContextOptionsBuilder<TContext>().UseNpgsql(ConnectionString).Options)!;

    internal TestDb NewDb() => new(new DbContextOptionsBuilder<TestDb>().UseNpgsql(ConnectionString).Options);

    /// <summary>Tests of one class run sequentially, so each starts from empty tables.</summary>
    internal async Task ResetAsync()
    {
        await using var db = NewDb();
        var ct = TestContext.Current.CancellationToken;
        await db.Things.ExecuteDeleteAsync(ct);
        foreach (var outbox in new[] { "Orders", "Billing" })
        {
            await db.Set<OutboxMessage>(MessagingNames.Outbox(outbox)).ExecuteDeleteAsync(ct);
        }

        foreach (var inbox in new[] { "Orders", "Shipping" })
        {
            await db.Set<InboxMessage>(MessagingNames.Inbox(inbox)).ExecuteDeleteAsync(ct);
        }
    }

    internal async Task<OutboxMessage> OutboxAsync(string outbox, Guid id)
    {
        await using var db = NewDb();
        return await db.Set<OutboxMessage>(MessagingNames.Outbox(outbox)).AsNoTracking()
            .SingleAsync(m => m.Id == id, TestContext.Current.CancellationToken);
    }

    internal async Task<List<OutboxMessage>> OutboxRowsAsync(string outbox)
    {
        await using var db = NewDb();
        return await db.Set<OutboxMessage>(MessagingNames.Outbox(outbox)).AsNoTracking()
            .OrderBy(m => m.OccurredAt).ToListAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<List<InboxMessage>> InboxRowsAsync(string inbox)
    {
        await using var db = NewDb();
        return await db.Set<InboxMessage>(MessagingNames.Inbox(inbox)).AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<List<string>> ThingNamesAsync()
    {
        await using var db = NewDb();
        return await db.Things.AsNoTracking().Select(t => t.Name).OrderBy(n => n).ToListAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<OutboxMessage> SeedOutboxAsync(
        string outbox,
        string type = "test.thing-happened",
        int version = 1,
        string? payloadJson = null,
        OutboxStatus status = OutboxStatus.Pending,
        int attempts = 0,
        DateTimeOffset? occurredAt = null,
        DateTimeOffset? nextAttemptAt = null,
        string? leaseOwner = null,
        DateTimeOffset? leaseExpiresAt = null)
    {
        await using var db = NewDb();
        var message = new OutboxMessage
        {
            Type = type,
            Version = version,
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            PayloadJson = payloadJson ?? "{\"thingId\":\"" + Guid.NewGuid() + "\"}",
            Status = status,
            Attempts = attempts,
            NextAttemptAt = nextAttemptAt,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = leaseExpiresAt,
        };
        db.Set<OutboxMessage>(MessagingNames.Outbox(outbox)).Add(message);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return message;
    }

    internal async Task SeedInboxAsync(string inbox, Guid eventId, string handler, DateTimeOffset processedAt)
    {
        await using var db = NewDb();
        db.Set<InboxMessage>(MessagingNames.Inbox(inbox)).Add(new InboxMessage { EventId = eventId, Handler = handler, ProcessedAt = processedAt });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Declares every outbox and inbox of <see cref="TestDb"/>, as a host does once per module.</summary>
    internal static void DeclareAll(MessagingBuilder messaging)
    {
        messaging.Outbox<TestDb>("Orders");
        messaging.Outbox<TestDb>("Billing");
        messaging.Inbox<TestDb>("Orders");
        messaging.Inbox<TestDb>("Shipping");
    }

    /// <summary>Registers the standard test wiring: ThingHappened goes out through "Orders" and is handled by
    /// <see cref="FirstHandler"/> (inbox "Shipping") and <see cref="SecondHandler"/> (inbox "Orders");
    /// OtherHappened goes out through "Billing".</summary>
    internal static void Standard(MessagingBuilder messaging)
    {
        messaging.Publishes<ThingHappened>("Orders");
        messaging.Publishes<OtherHappened>("Billing");
        messaging.Subscribe<ThingHappened, FirstHandler>("Shipping");
        messaging.Subscribe<ThingHappened, SecondHandler>("Orders");
        messaging.Subscribe<OtherHappened, OtherHandler>("Orders");
    }

    /// <summary>Starts a host running the block against this database.</summary>
    internal async Task<MessagingHost> StartAsync(
        Action<MessagingBuilder>? configure = null,
        Action<IServiceCollection>? services = null,
        Probe? probe = null,
        bool dispatcherEnabled = true,
        TimeSpan? lease = null,
        TimeSpan? poll = null,
        TimeSpan? maxBackoff = null,
        int? maxAttempts = null,
        CapturingLoggerProvider? logs = null,
        bool start = true,
        bool declareStandard = true)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Messaging:DispatcherEnabled"] = dispatcherEnabled ? "true" : "false",
            ["Messaging:LeaseDuration"] = (lease ?? TimeSpan.FromSeconds(10)).ToString("c", CultureInfo.InvariantCulture),
            ["Messaging:SafetyNetPollInterval"] = (poll ?? TimeSpan.FromSeconds(5)).ToString("c", CultureInfo.InvariantCulture),
            ["Messaging:MaxBackoff"] = (maxBackoff ?? TimeSpan.FromSeconds(1)).ToString("c", CultureInfo.InvariantCulture),
        };
        if (maxAttempts is { } m)
        {
            values["Messaging:MaxAttempts"] = m.ToString(CultureInfo.InvariantCulture);
        }

        var collection = new ServiceCollection();
        collection.AddLogging(b =>
        {
            if (logs is not null)
            {
                b.AddProvider(logs);
            }
        });
        collection.AddSingleton(probe ?? new Probe());
        collection.AddDbContext<TestDb>((sp, options) => options.UseNpgsql(ConnectionString).AddMessagingInterceptors(sp));
        collection.AddMessaging(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Messaging"),
            b =>
            {
                if (declareStandard)
                {
                    DeclareAll(b);
                }

                (configure ?? Standard)(b);
            });
        services?.Invoke(collection);

        var host = new MessagingHost(collection.BuildServiceProvider());
        if (start)
        {
            await host.StartAsync();
        }

        return host;
    }
}

/// <summary>A DI container with the block registered and its hosted dispatcher running.</summary>
internal sealed class MessagingHost(ServiceProvider provider) : IAsyncDisposable
{
    public IServiceProvider Services => provider;

    public async Task StartAsync()
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }
    }

    /// <summary>Publishes in a fresh scope and commits with a plain <c>SaveChangesAsync</c>.</summary>
    public async Task<Guid> PublishAsync<TEvent>(TEvent evt)
        where TEvent : class
    {
        using var scope = provider.CreateScope();
        var id = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().Publish(evt);
        await scope.ServiceProvider.GetRequiredService<TestDb>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StopAsync(CancellationToken.None);
        }

        await provider.DisposeAsync();
    }
}

internal static class Wait
{
    public static async Task UntilAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(limit);
        while (!await condition())
        {
            if (cts.IsCancellationRequested)
            {
                Assert.Fail($"Timed out after {limit}: {because}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }
    }

    public static async Task UntilDeliveredAsync(PostgresFixture fixture, string outbox, Guid id) =>
        await UntilAsync(
            async () => (await fixture.OutboxAsync(outbox, id)).Status == OutboxStatus.Delivered,
            $"event {id} delivered");
}
