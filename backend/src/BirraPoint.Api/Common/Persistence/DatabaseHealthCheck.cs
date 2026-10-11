using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BirraPoint.Api.Common.Persistence;

/// <summary>
/// Readiness check "db" (FR-048): can the API open a connection to Postgres. Result carries no
/// exception or connection details; failures are logged by the health-check service without them.
/// </summary>
public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        try
        {
            return await db.Database.CanConnectAsync(cts.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database unreachable.");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database check timed out.");
        }
    }
}
