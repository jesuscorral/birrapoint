using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Common.Persistence;

/// <summary>
/// Stops startup when the database was built from the pre-baseline migrations (FR-070). Without it the new
/// history in <c>migrations</c> is empty, so EF would create fresh tables in the module schemas next to the
/// old ones in <c>public</c>: a silent mix. The developer resets the BirraPoint database instead (FR-071).
/// <para>
/// An applied migration this build does not know is refused only when it sorts before the earliest known one:
/// that is the old baseline. A newer unknown id means the database is ahead of the build (rollback to an
/// older image, checkout of an older branch), which is not a legacy database and must keep starting (FR-091).
/// </para>
/// </summary>
internal static class LegacyDatabaseGuard
{
    public const string ResetCommandName = "Reset database";

    /// <summary>Pure decision: returns the refusal message, or <c>null</c> when startup may proceed.</summary>
    public static string? Evaluate(bool legacyHistoryExists, IEnumerable<string> applied, IEnumerable<string> known)
    {
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(known);

        var knownSet = known.ToHashSet(StringComparer.Ordinal);
        var earliestKnown = knownSet.Count == 0 ? null : knownSet.Min(StringComparer.Ordinal);
        var unknown = applied.FirstOrDefault(id =>
            !knownSet.Contains(id)
            && (earliestKnown is null || string.CompareOrdinal(id, earliestKnown) < 0));

        if (!legacyHistoryExists && unknown is null)
        {
            return null;
        }

        var reason = legacyHistoryExists
            ? "its migration history is still in the public schema"
            : $"it has the migration '{unknown}', which this build does not know";
        return $"The database was built from a previous migration baseline ({reason}). " +
               $"Run the '{ResetCommandName}' command on the 'db' resource in the Aspire dashboard " +
               "(it drops and recreates only the birrapoint database, Keycloak is untouched), then restart the API.";
    }

    public static async Task EnsureCompatibleAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var connection = context.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var legacy = await TableExistsAsync(connection, "public.\"__EFMigrationsHistory\"", cancellationToken);
            var hasNewHistory = await TableExistsAsync(connection, $"{DbSchemas.Migrations}.\"__EFMigrationsHistory\"", cancellationToken);

            var applied = new List<string>();
            if (hasNewHistory)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT \"MigrationId\" FROM migrations.\"__EFMigrationsHistory\"";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    applied.Add(reader.GetString(0));
                }
            }

            var known = context.Database.GetMigrations();
            var problem = Evaluate(legacy, applied, known);
            if (problem is not null)
            {
                throw new InvalidOperationException(problem);
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
