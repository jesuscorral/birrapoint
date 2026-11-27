using System.Text.Json;
using Npgsql;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>One <c>OutboxMessages</c> row as stored (data-model.md §OutboxMessage / InboxMessage).</summary>
internal sealed record OutboxRow(Guid Id, string Type, int Version, string Status, int Attempts, JsonElement Payload)
{
    /// <summary>Reads a payload property regardless of the casing the serializer chose.</summary>
    public JsonElement Property(string name)
    {
        foreach (var property in Payload.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new KeyNotFoundException($"Payload of {Type} has no '{name}' property: {Payload}");
    }
}

/// <summary>
/// Raw SQL against the messaging tables, so the tests depend on the stored contract (table and column names
/// of the living data-model) and not on any block type. Schemas and table names are fixed constants, never input.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Test-only helper: SQL text is built from fixed schema constants, values are always parameters.")]
internal static class MessagingDb
{
    public const string Organization = "organization";
    public const string Judging = "judging";
    public const string Publication = "publication";

    public const string CompetitionFinalized = "organization.competition-finalized";
    public const string JudgeEmailChanged = "organization.judge-email-changed";
    public const string TableJudgesChanged = "judging.table-judges-changed";
    public const string EvaluationCorrected = "judging.evaluation-corrected";

    public static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    public static async Task<object?> ScalarAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    public static async Task<long> CountAsync(string connectionString, string sql, params (string Name, object Value)[] parameters) =>
        (long)(await ScalarAsync(connectionString, sql, parameters))!;

    public static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Outbox rows of <paramref name="type"/> in <paramref name="schema"/>, oldest first, optionally
    /// limited to the ones whose payload names <paramref name="competitionId"/>.</summary>
    public static async Task<IReadOnlyList<OutboxRow>> OutboxAsync(
        string connectionString, string schema, string type, Guid? competitionId = null)
    {
        var rows = new List<OutboxRow>();
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(
            $"SELECT \"Id\", \"Type\", \"Version\", \"Status\", \"Attempts\", \"PayloadJson\"::text FROM {schema}.\"OutboxMessages\" " +
            "WHERE \"Type\" = @type ORDER BY \"OccurredAt\", \"Id\"", connection);
        command.Parameters.AddWithValue("type", type);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            using var document = JsonDocument.Parse(reader.GetString(5));
            var row = new OutboxRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetInt32(4),
                document.RootElement.Clone());
            if (competitionId is null || row.Property("competitionId").GetGuid() == competitionId)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    public static Task<long> InboxCountAsync(string connectionString, string schema, Guid eventId) =>
        CountAsync(connectionString, $"SELECT count(*) FROM {schema}.\"InboxMessages\" WHERE \"EventId\" = @id", ("id", eventId));

    /// <summary>Forces a redelivery exactly as an operator or a lost acknowledgement would.</summary>
    public static Task ForceRedeliveryAsync(string connectionString, string schema, Guid eventId) =>
        ExecuteAsync(
            connectionString,
            $"UPDATE {schema}.\"OutboxMessages\" SET \"Status\" = 'Pending', \"NextAttemptAt\" = NULL WHERE \"Id\" = @id",
            ("id", eventId));

    /// <summary>Waits until the event is <c>Delivered</c> (again, after a forced redelivery).</summary>
    public static Task AwaitDeliveredAsync(string connectionString, string schema, Guid eventId) =>
        EventualAssert.EqualAsync(
            "Delivered",
            async () => (string)(await ScalarAsync(
                connectionString, $"SELECT \"Status\" FROM {schema}.\"OutboxMessages\" WHERE \"Id\" = @id", ("id", eventId)))!,
            $"event {eventId} delivered");

    public static Task<long> JobCountAsync(string connectionString, Guid competitionId, string type) =>
        CountAsync(
            connectionString,
            "SELECT count(*) FROM jobs.\"DispatchJobs\" WHERE \"CompetitionId\" = @group AND \"Type\" = @type",
            ("group", competitionId), ("type", type));
}
