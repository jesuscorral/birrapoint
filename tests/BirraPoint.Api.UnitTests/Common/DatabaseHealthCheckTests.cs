using BirraPoint.Api.Common.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace BirraPoint.Api.UnitTests.Common;

public sealed class DatabaseHealthCheckTests
{
    private static NpgsqlConnection UnreachableConnection() =>
        new("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1");

    [Fact]
    public async Task CallerCancellationPropagatesInsteadOfBeingReportedAsTimeout()
    {
        await using var connection = UnreachableConnection();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DatabaseHealthCheck(connection).CheckHealthAsync(new HealthCheckContext(), cts.Token));
    }

    [Fact]
    public async Task UnreachableDatabaseIsReportedUnhealthyWithoutDetails()
    {
        await using var connection = UnreachableConnection();

        var result = await new DatabaseHealthCheck(connection).CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
    }
}
