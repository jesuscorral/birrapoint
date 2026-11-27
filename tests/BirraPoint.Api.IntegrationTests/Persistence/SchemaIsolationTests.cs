using BirraPoint.Api.IntegrationTests.TestHost;
using Npgsql;

namespace BirraPoint.Api.IntegrationTests.Persistence;

/// <summary>
/// The database mirrors the module boundaries (FR-092, FR-093): no foreign key crosses a schema, and each
/// schema holds exactly the tables its owner declares in the living data model. Runs on the migrated test
/// database, so it also pins what the baseline migrations create.
/// </summary>
public sealed class SchemaIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string CrossSchemaForeignKeys =
        """
        SELECT tc.table_schema, tc.table_name, ccu.table_schema AS ref_schema, ccu.table_name AS ref_table
        FROM information_schema.table_constraints tc
        JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = tc.constraint_name
        WHERE tc.constraint_type = 'FOREIGN KEY' AND ccu.table_schema <> tc.table_schema
        ORDER BY 1, 2, 3, 4
        """;

    // data-model.md §Schemas
    private static readonly Dictionary<string, string[]> ExpectedTables = new(StringComparer.Ordinal)
    {
        ["catalog"] = ["BjcpStyles"],
        ["organization"] =
        [
            "Competitions", "Organizers", "CompetitionCategories", "CompetitionCategoryStyles", "Participants",
            "BeerEntries", "EntryCollaborators", "ImportBatches", "ImportRows", "Judges", "Invitations",
            "JudgeImportBatches", "JudgeImportRows", "OutboxMessages", "InboxMessages",
        ],
        ["judging"] =
        [
            "TastingTables", "TableJudges", "TableSamples", "Evaluations", "DiscrepancyAlerts", "OutboxMessages",
            "InboxMessages",
        ],
        ["publication"] = ["GeneratedScoreSheets", "ResultsArchives", "InboxMessages"],
        ["jobs"] = ["DispatchJobs"],
        ["audit"] = ["AuditLogs"],
    };

    [Fact]
    public async Task NoForeignKeyReferencesATableInAnotherSchema()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(CrossSchemaForeignKeys, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var offenders = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            offenders.Add($"{reader.GetString(0)}.{reader.GetString(1)} -> {reader.GetString(2)}.{reader.GetString(3)}");
        }

        Assert.True(offenders.Count == 0, $"{offenders.Count} cross-schema foreign keys: {string.Join("; ", offenders)}");
    }

    [Fact]
    public async Task EachModuleSchemaHoldsExactlyTheTablesOfTheLivingDataModel()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT table_schema, table_name FROM information_schema.tables
            WHERE table_type = 'BASE TABLE' AND table_schema = ANY(@schemas) AND table_name <> '__EFMigrationsHistory'
            """, connection);
        command.Parameters.AddWithValue("schemas", ExpectedTables.Keys.ToArray());
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var actual = ExpectedTables.Keys.ToDictionary(schema => schema, _ => new List<string>(), StringComparer.Ordinal);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            actual[reader.GetString(0)].Add(reader.GetString(1));
        }

        foreach (var (schema, expected) in ExpectedTables)
        {
            Assert.Equal(expected.Order(StringComparer.Ordinal), actual[schema].Order(StringComparer.Ordinal));
        }
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}
