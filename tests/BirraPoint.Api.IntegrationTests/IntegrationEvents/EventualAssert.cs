using Npgsql;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>
/// Polling assertions for effects that become eventually consistent once a module reacts to an
/// integration event instead of writing in the publisher's transaction (SC-023): the test waits for the
/// effect to appear instead of reading it at once. Reactions are expected within 1 s (FR-089), so the
/// default allowance is 5 s; only the PDF/ZIP/email pipeline passes a longer, explicit timeout.
/// </summary>
internal static class EventualAssert
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Allowance for waits that include the PDF, ZIP and email pipeline (not a cross-module reaction).</summary>
    public static readonly TimeSpan PipelineTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] OutboxSchemas = ["organization", "judging"];

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Waits until <paramref name="condition"/> holds; fails with <paramref name="because"/> otherwise.</summary>
    public static async Task TrueAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var limit = timeout ?? DefaultTimeout;
        using var cts = new CancellationTokenSource(limit);
        while (!await condition())
        {
            if (cts.IsCancellationRequested)
            {
                Assert.Fail($"Timed out after {limit}: {because}");
            }

            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Waits until <paramref name="read"/> returns <paramref name="expected"/>, reporting the last value seen.</summary>
    public static async Task EqualAsync<T>(T expected, Func<Task<T>> read, string because, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        var limit = timeout ?? DefaultTimeout;
        using var cts = new CancellationTokenSource(limit);
        while (true)
        {
            var last = await read();
            if (EqualityComparer<T>.Default.Equals(last, expected))
            {
                return;
            }

            if (cts.IsCancellationRequested)
            {
                Assert.Fail($"Timed out after {limit}: {because} (expected {expected}, last seen {last}).");
            }

            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Waits until <paramref name="read"/> returns a value accepted by <paramref name="accept"/> and returns it.</summary>
    public static async Task<T> ValueAsync<T>(
        Func<Task<T>> read, Func<T, bool> accept, string because, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(accept);
        var limit = timeout ?? DefaultTimeout;
        using var cts = new CancellationTokenSource(limit);
        while (true)
        {
            var value = await read();
            if (accept(value))
            {
                return value;
            }

            if (cts.IsCancellationRequested)
            {
                Assert.Fail($"Timed out after {limit}: {because}");
            }

            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Waits until no <c>Pending</c> or <c>Delivering</c> row is left in the module outboxes, so that a test
    /// asserting "nothing changed" does so after the reaction had its chance to run. Returns at once when the
    /// outbox tables do not exist yet (a database built before the messaging tables).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Schema names come from the fixed OutboxSchemas list; no input reaches the query.")]
    public static async Task OutboxDrainedAsync(string connectionString, TimeSpan? timeout = null)
    {
        await TrueAsync(async () =>
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            foreach (var schema in OutboxSchemas)
            {
                await using var exists = new NpgsqlCommand($"SELECT to_regclass('{schema}.\"OutboxMessages\"') IS NOT NULL", connection);
                if (!(bool)(await exists.ExecuteScalarAsync(TestContext.Current.CancellationToken))!)
                {
                    continue;
                }

                await using var open = new NpgsqlCommand(
                    $"SELECT count(*) FROM {schema}.\"OutboxMessages\" WHERE \"Status\" IN ('Pending', 'Delivering')", connection);
                if ((long)(await open.ExecuteScalarAsync(TestContext.Current.CancellationToken))! > 0)
                {
                    return false;
                }
            }

            return true;
        }, "every outbox row delivered", timeout);
    }
}
