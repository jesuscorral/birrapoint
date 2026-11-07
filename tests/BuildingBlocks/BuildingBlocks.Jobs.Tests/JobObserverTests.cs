using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Jobs.Tests;

/// <summary>Observers are told about each persisted state change; they never decide a job's outcome.</summary>
public sealed class JobObserverTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Type = "Observed";

    [Fact]
    public async Task ObserverIsToldAboutCompletionAfterTheOutcomeIsPersisted()
    {
        await fixture.ResetAsync();
        var group = Guid.NewGuid();
        var job = await fixture.SeedAsync(Type, groupKey: group);
        var statusSeenInDb = new TaskCompletionSource<JobStatus>();
        var observer = new RecordingObserver(async changed =>
            statusSeenInDb.TrySetResult((await fixture.LoadAsync(changed.Id)).Status));

        await using var worker = await fixture.StartWorkerAsync(s =>
        {
            s.AddSingleton<IJobHandler>(new CountingHandler(Type));
            s.AddSingleton<IJobObserver>(observer);
        });

        Assert.Equal(JobStatus.Completed, await statusSeenInDb.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
        var change = Assert.Single(observer.Changes);
        Assert.Equal((job.Id, Type, group, JobStatus.Completed, (string?)null), change);
    }

    [Fact]
    public async Task ObserverIsToldAboutEachFailedAttemptWithTheError()
    {
        await fixture.ResetAsync();
        var job = await fixture.SeedAsync(Type);
        var observer = new RecordingObserver();

        await using var worker = await fixture.StartWorkerAsync(s =>
        {
            s.AddSingleton<IJobHandler>(new DelegateHandler(Type, (_, _) => throw new InvalidOperationException("boom")));
            s.AddSingleton<IJobObserver>(observer);
        }, maxAttempts: 2);

        await Wait.UntilAsync(async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Failed, "job failed", TimeSpan.FromSeconds(30));
        await Wait.UntilAsync(() => Task.FromResult(observer.Changes.Count == 2), "both attempts announced");

        Assert.Equal(
            [(JobStatus.Pending, "boom"), (JobStatus.Failed, "boom")],
            observer.Changes.Select(c => (c.Status, c.Detail)).ToArray());
    }

    [Fact]
    public async Task ObserverFailureNeverRevertsTheOutcomeNorSilencesOtherObservers()
    {
        await fixture.ResetAsync();
        var job = await fixture.SeedAsync(Type);
        var healthy = new RecordingObserver();

        await using var worker = await fixture.StartWorkerAsync(s =>
        {
            s.AddSingleton<IJobHandler>(new CountingHandler(Type));
            s.AddSingleton<IJobObserver>(new RecordingObserver(_ => throw new InvalidOperationException("observer down")));
            s.AddSingleton<IJobObserver>(healthy);
        });

        await Wait.UntilAsync(async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Completed, "job completed");
        await Wait.UntilAsync(() => Task.FromResult(!healthy.Changes.IsEmpty), "healthy observer told");

        Assert.Equal(JobStatus.Completed, (await fixture.LoadAsync(job.Id)).Status);
    }
}
