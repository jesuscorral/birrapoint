using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Jobs.Tests;

/// <summary>The host decides the queue's schema through <c>ApplyJobs(configure)</c>; the worker's raw claim SQL
/// follows the model, so enqueue, claim and completion all work in a non-default schema.</summary>
public sealed class HostSchemaTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task QueueTableLivesInTheHostSetSchema()
    {
        await using var db = fixture.NewDb();

        var found = await db.Database
            .SqlQuery<int>($"""SELECT 1 AS "Value" FROM information_schema.tables WHERE table_schema = {TestDb.SchemaName} AND table_name = {TestDb.TableName}""")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(found);
    }

    [Fact]
    public async Task EnqueuedJobIsClaimedAndCompletedInTheHostSetSchema()
    {
        await fixture.ResetAsync();
        var handler = new CountingHandler("Work");
        await using var host = await fixture.StartWorkerAsync(s => s.AddSingleton<IJobHandler>(handler));

        await using var db = fixture.NewDb();
        var queue = new JobQueue<TestDb>(db, host.Services.GetRequiredService<IJobWakeUp>());
        var group = Guid.NewGuid();
        await queue.EnqueueAsync(group, "Work", new { }, TestContext.Current.CancellationToken);

        await Wait.UntilAsync(async () =>
        {
            await using var verify = fixture.NewDb();
            return await verify.Jobs.AnyAsync(
                j => j.GroupKey == group && j.Status == JobStatus.Completed, TestContext.Current.CancellationToken);
        }, "job completed");

        Assert.Single(handler.Calls);
    }
}
