using System.Data.Common;
using BirraPoint.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Common.Persistence;

/// <summary>
/// Stops startup when the database was built from a previous migration baseline. Without it the new per-context
/// histories are empty, so EF would create fresh tables next to the old ones: a silent mix. The developer resets the
/// BirraPoint database instead (FR-071, FR-098).
/// <para>
/// Refused: the pre-baseline history in <c>public</c> (FR-070) and the single shared history in <c>migrations</c>
/// (FR-098). Per context, an applied migration this build does not know is refused only when it sorts before the
/// earliest known one: that is an old baseline. A newer unknown id means the database is ahead of the build (rollback
/// to an older image, checkout of an older branch), which is not a legacy database and must keep starting (FR-091).
/// </para>
/// </summary>
internal static class LegacyDatabaseGuard
{
    public const string ResetCommandName = "Reset database";

    /// <summary>Pure decision: returns the refusal message, or <c>null</c> when startup may proceed.</summary>
    public static string? Evaluate(
        bool publicHistoryExists, bool sharedMigrationsHistoryExists, IEnumerable<string> applied, IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(known);

        var knownSet = known.ToHashSet(StringComparer.Ordinal);
        var earliestKnown = knownSet.Count == 0 ? null : knownSet.Min(StringComparer.Ordinal);
        var unknown = applied.FirstOrDefault(id =>
            !knownSet.Contains(id)
            && (earliestKnown is null || string.CompareOrdinal(id, earliestKnown) < 0));

        if (!publicHistoryExists && !sharedMigrationsHistoryExists && unknown is null)
        {
            return null;
        }

        string reason;
        if (publicHistoryExists)
        {
            reason = "its migration history is still in the public schema";
        }
        else if (sharedMigrationsHistoryExists)
        {
            reason = $"its migration history is still the shared one in the {DbSchemas.LegacyMigrations} schema";
        }
        else
        {
            reason = $"it has the migration '{unknown}', which this build does not know";
        }

        return $"The database was built from a previous migration baseline ({reason}). " +
               $"Run the '{ResetCommandName}' command on the 'db' resource in the Aspire dashboard " +
               "(it drops and recreates only the birrapoint database, Keycloak is untouched), then restart the API.";
    }

    /// <summary>Checks the legacy schemas once, then each context's own history against the migrations it knows.</summary>
    public static async Task EnsureCompatibleAsync(IReadOnlyList<BirraPointDbContext> contexts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        if (contexts.Count == 0)
        {
            return;
        }

        var connection = contexts[0].Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var publicHistory = await TableExistsAsync(connection, "public.\"__EFMigrationsHistory\"", cancellationToken);
            var sharedHistory = await TableExistsAsync(connection, $"{DbSchemas.LegacyMigrations}.\"__EFMigrationsHistory\"", cancellationToken);

            if (publicHistory || sharedHistory)
            {
                throw new InvalidOperationException(Evaluate(publicHistory, sharedHistory, applied: [], known: [])!);
            }

            foreach (var context in contexts)
            {
                var applied = await ReadAppliedAsync(connection, context.Schema, cancellationToken);
                var problem = Evaluate(publicHistoryExists: false, sharedMigrationsHistoryExists: false, applied, context.Database.GetMigrations());
                if (problem is not null)
                {
                    throw new InvalidOperationException(problem);
                }
            }
        }
        finally
        {
            if (!wasOpen)
            {
                await connection.CloseAsync();
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The schema is the Schema constant of one of the six contexts (DbSchemas), never user input.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S2077:Formatting SQL queries is security-sensitive",
        Justification = "The schema is the Schema constant of one of the six contexts (DbSchemas), never user input.")]
    private static async Task<List<string>> ReadAppliedAsync(DbConnection connection, string schema, CancellationToken cancellationToken)
    {
        var applied = new List<string>();
        if (!await TableExistsAsync(connection, $"{schema}.\"__EFMigrationsHistory\"", cancellationToken))
        {
            return applied;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"MigrationId\" FROM {schema}.\"__EFMigrationsHistory\"";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string qualifiedName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass(@name) IS NOT NULL";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = qualifiedName;
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
