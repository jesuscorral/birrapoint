using System.Collections.Concurrent;
using System.Globalization;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>
/// A subscriber that keeps failing, registered in the real host, is retried up to <c>Messaging:MaxAttempts</c>,
/// the row ends <c>Dead</c> with one error log that names the event type, id and subscriber and never the
/// payload, and a later event is still delivered (FR-080, SC-022).
/// </summary>
public sealed class DeadLetterHostTests(ApiFactory factory) : IntegrationEventsTestBase(factory), IClassFixture<ApiFactory>
{
    private const int MaxAttempts = 3;
    private const string ProbeType = "organization.dead-letter-probe";
    private const string SecretText = "SECRET-PAYLOAD-TEXT";

    [IntegrationEvent(ProbeType, 1)]
    internal sealed record DeadLetterProbe(Guid CompetitionId, string Secret);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by the messaging block through dependency injection.")]
    internal sealed class AlwaysFailingSubscriber : IIntegrationEventHandler<DeadLetterProbe>
    {
        public Task HandleAsync(DeadLetterProbe integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("subscriber unavailable");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Entries);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<(LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }

    private WebApplicationFactory<Program> NewFailingHost(CapturingLoggerProvider logs) =>
        NewHost(configureServices: services => services.ConfigureMessaging(m =>
        {
            m.Publishes<DeadLetterProbe>("Organization");
            m.Subscribe<DeadLetterProbe, AlwaysFailingSubscriber>("Judging");
        })).WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:MaxAttempts"] = MaxAttempts.ToString(CultureInfo.InvariantCulture),
                ["Messaging:MaxBackoff"] = "00:00:01",
            }));
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

    private static async Task<Guid> PublishProbeAsync(IServiceProvider services, Guid competitionId)
    {
        await using var scope = services.CreateAsyncScope();
        var id = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>()
            .Publish(new DeadLetterProbe(competitionId, SecretText));
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private Task<string> StatusAsync(Guid eventId) =>
        MessagingDb.ScalarAsync(
            ConnectionString, "SELECT \"Status\" FROM organization.\"OutboxMessages\" WHERE \"Id\" = @id", ("id", eventId))
            .ContinueWith(t => (string)t.Result!, TaskScheduler.Default);

    [Fact]
    public async Task AnAlwaysFailingSubscriberEndsDeadWithOneErrorLogThatNamesTypeIdAndSubscriberButNotThePayload()
    {
        using var logs = new CapturingLoggerProvider();
        await using var host = NewFailingHost(logs);
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var competitionId = await CreateCompetitionAsync(organizer, "DeadLetter");

        var id = await PublishProbeAsync(host.Services, competitionId);

        await EventualAssert.EqualAsync("Dead", () => StatusAsync(id), "event dead after the last attempt", TimeSpan.FromSeconds(20));
        Assert.Equal(
            MaxAttempts,
            (int)(await MessagingDb.ScalarAsync(
                ConnectionString, "SELECT \"Attempts\" FROM organization.\"OutboxMessages\" WHERE \"Id\" = @id", ("id", id)))!);
        var lastError = (string?)await MessagingDb.ScalarAsync(
            ConnectionString, "SELECT \"LastError\" FROM organization.\"OutboxMessages\" WHERE \"Id\" = @id", ("id", id));
        Assert.DoesNotContain(SecretText, lastError ?? string.Empty, StringComparison.Ordinal);

        var error = Assert.Single(
            logs.Entries, e => e.Level >= LogLevel.Error && e.Message.Contains(id.ToString(), StringComparison.Ordinal));
        Assert.Contains(ProbeType, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AlwaysFailingSubscriber), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains(SecretText, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnEventPublishedAfterADeadOneIsStillDelivered()
    {
        using var logs = new CapturingLoggerProvider();
        await using var host = NewFailingHost(logs);
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var competitionId = await CreateCompetitionAsync(organizer, "DeadLetterThen");
        var dead = await PublishProbeAsync(host.Services, competitionId);
        await EventualAssert.EqualAsync("Dead", () => StatusAsync(dead), "probe parked as dead", TimeSpan.FromSeconds(20));

        var closed = await SeedClosedCompetitionAsync(host, organizer, "AfterDead");
        var response = await TransitionAsync(organizer, closed.CompetitionId, "Finalized");
        Assert.True(response.IsSuccessStatusCode, $"Unexpected {(int)response.StatusCode}.");

        var row = Assert.Single(await MessagingDb.OutboxAsync(
            ConnectionString, MessagingDb.Organization, MessagingDb.CompetitionFinalized, closed.CompetitionId));
        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Organization, row.Id);
        Assert.Equal("Dead", await StatusAsync(dead));
    }
}
