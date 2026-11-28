using System.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace BirraPoint.Api.Common.Persistence;

/// <summary>
/// Readiness check "db" (FR-048): can the API open a connection to Postgres. Uses the scope's shared connection, so it
/// depends on no data context. Result carries no exception or connection details; failures are logged by the
/// health-check service without them.
/// </summary>
internal sealed class DatabaseHealthCheck(NpgsqlConnection connection) : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        var openedHere = connection.State != ConnectionState.Open;
        try
        {
            if (openedHere)
            {
                await connection.OpenAsync(cts.Token);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cts.Token);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Database check timed out.");
        }
        catch (Exception ex) when (ex is NpgsqlException or System.Net.Sockets.SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Database unreachable.");
        }
        finally
        {
            if (openedHere && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }
    }
}
