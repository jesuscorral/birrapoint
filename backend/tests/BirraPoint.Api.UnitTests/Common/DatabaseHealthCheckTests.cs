using BirraPoint.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BirraPoint.Api.UnitTests.Common;

public sealed class DatabaseHealthCheckTests
{
    private static AppDbContext UnreachableDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")
        .Options);

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_being_reported_as_timeout()
    {
        await using var db = UnreachableDb();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext(), cts.Token));
    }
}
