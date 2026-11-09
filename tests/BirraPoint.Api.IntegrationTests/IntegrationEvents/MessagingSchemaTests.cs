using BirraPoint.Api.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>After migrating, the module schemas carry the outbox and inbox tables of the living data-model, with
/// their columns and indexes (FR-076, FR-079, FR-090).</summary>
public sealed class MessagingSchemaTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly string[] OutboxColumns =
    [
        "Attempts:integer", "Id:uuid", "LastError:character varying", "LeaseExpiresAt:timestamp with time zone",
        "LeaseOwner:character varying", "NextAttemptAt:timestamp with time zone", "OccurredAt:timestamp with time zone",
        "PayloadJson:jsonb", "Status:character varying", "Type:character varying", "Version:integer",
    ];

    private static readonly string[] InboxColumns =
    [
        "EventId:uuid", "Handler:character varying", "ProcessedAt:timestamp with time zone",
    ];

    private async Task<string[]> ColumnsAsync(string schema, string table)
    {
        await using var db = new BirraPoint.Api.Common.Persistence.AppDbContext(fixture.Options);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT column_name || ':' || data_type FROM information_schema.columns " +
            "WHERE table_schema = @schema AND table_name = @table ORDER BY column_name", connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return [.. columns.Order(StringComparer.Ordinal)];
    }

    private async Task<string[]> IndexesAsync(string schema, string table)
    {
        await using var db = new BirraPoint.Api.Common.Persistence.AppDbContext(fixture.Options);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT indexname FROM pg_indexes WHERE schemaname = @schema AND tablename = @table ORDER BY indexname", connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);
        var indexes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            indexes.Add(reader.GetString(0));
        }

        return [.. indexes.Order(StringComparer.Ordinal)];
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("judging")]
    public async Task PublishingModulesOwnAnOutboxWithTheDocumentedColumnsAndIndexes(string schema)
    {
        Assert.Equal(OutboxColumns, await ColumnsAsync(schema, "OutboxMessages"));
        Assert.Equal(
            [
                $"IX_{schema}_OutboxMessages_Status_LeaseExpiresAt",
                $"IX_{schema}_OutboxMessages_Status_NextAttemptAt",
                $"PK_{schema}_OutboxMessages",
            ],
            await IndexesAsync(schema, "OutboxMessages"));
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("judging")]
    [InlineData("publication")]
    public async Task SubscribingModulesOwnAnInboxKeyedByEventAndHandler(string schema)
    {
        Assert.Equal(InboxColumns, await ColumnsAsync(schema, "InboxMessages"));
        Assert.Equal(
            [$"IX_{schema}_InboxMessages_ProcessedAt", $"PK_{schema}_InboxMessages"],
            await IndexesAsync(schema, "InboxMessages"));
    }

    [Fact]
    public async Task PublicationHasNoOutbox() =>
        Assert.Empty(await ColumnsAsync("publication", "OutboxMessages"));
}
