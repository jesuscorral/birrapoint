using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Jobs.Tests;

public sealed class JobQueueTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task StageAddsARowThatOnlyCommitsWithTheCallersSaveChanges()
    {
        await fixture.ResetAsync();
        await using var db = fixture.NewDb();
        var wakeUp = new SpyWakeUp();
        var queue = new JobQueue<TestDb>(db, wakeUp);
        var group = Guid.NewGuid();

        queue.Stage(group, "SendInvitation", new { JudgeId = 7 });

        await using (var other = fixture.NewDb())
        {
            Assert.False(await other.Jobs.AnyAsync(j => j.GroupKey == group, TestContext.Current.CancellationToken));
        }

        var before = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var verify = fixture.NewDb();
        var stored = await verify.Jobs.AsNoTracking().SingleAsync(j => j.GroupKey == group, TestContext.Current.CancellationToken);
        Assert.Equal("SendInvitation", stored.Type);
        Assert.Equal(JobStatus.Pending, stored.Status);
        Assert.Equal(0, stored.Attempts);
        Assert.Null(stored.LeaseOwner);
        Assert.True(stored.CreatedAt >= before.AddSeconds(-1));
        Assert.Equal(stored.CreatedAt, stored.UpdatedAt);
        using var payload = JsonDocument.Parse(stored.PayloadJson);
        Assert.Equal(7, payload.RootElement.GetProperty("JudgeId").GetInt32());
        Assert.Equal(0, wakeUp.Signals); // the caller signals after its own commit
    }

    [Fact]
    public async Task EnqueueSavesTheRowAndWakesTheWorker()
    {
        await fixture.ResetAsync();
        await using var db = fixture.NewDb();
        var wakeUp = new SpyWakeUp();
        var queue = new JobQueue<TestDb>(db, wakeUp);
        var group = Guid.NewGuid();

        await queue.EnqueueAsync(group, "GeneratePdfs", new { }, TestContext.Current.CancellationToken);

        await using var verify = fixture.NewDb();
        var stored = await verify.Jobs.AsNoTracking().SingleAsync(j => j.GroupKey == group, TestContext.Current.CancellationToken);
        Assert.Equal("GeneratePdfs", stored.Type);
        Assert.Equal(JobStatus.Pending, stored.Status);
        Assert.Equal(1, wakeUp.Signals);
    }
}
