using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace BuildingBlocks.Jobs.Tests;

/// <summary>
/// A host-agnostic DbContext whose table, schema and every column name differ from the block defaults, so any
/// worker SQL that hard-codes a name instead of reading the EF model fails against it.
/// </summary>
internal sealed class TestDb(DbContextOptions<TestDb> options) : DbContext(options)
{
    public const string TableName = "bg_queue";
    public const string SchemaName = "work";

    public DbSet<Job> Jobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyJobs(job =>
        {
            job.ToTable(TableName, SchemaName);
            job.Property(j => j.Id).HasColumnName("job_id");
            job.Property(j => j.GroupKey).HasColumnName("grp");
            job.Property(j => j.Type).HasColumnName("kind").HasMaxLength(40);
            job.Property(j => j.PayloadJson).HasColumnName("body");
            job.Property(j => j.Status).HasColumnName("state");
            job.Property(j => j.Attempts).HasColumnName("tries");
            job.Property(j => j.LastError).HasColumnName("err");
            job.Property(j => j.NextAttemptAt).HasColumnName("due_at");
            job.Property(j => j.LeaseOwner).HasColumnName("owner");
            job.Property(j => j.LeaseExpiresAt).HasColumnName("lease_until");
            job.Property(j => j.CreatedAt).HasColumnName("created");
            job.Property(j => j.UpdatedAt).HasColumnName("touched");
        });
}

/// <summary>One PostgreSQL 16 container per test class; schema created from the test model.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    internal string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = NewDb();
        await db.Database.EnsureCreatedAsync();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => _container.DisposeAsync();

    internal TestDb NewDb() => new(new DbContextOptionsBuilder<TestDb>().UseNpgsql(ConnectionString).Options);

    /// <summary>Tests of one class run sequentially, so each starts from an empty queue.</summary>
    internal async Task ResetAsync()
    {
        await using var db = NewDb();
        await db.Jobs.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
    }

    internal async Task<Job> SeedAsync(
        string type, JobStatus status = JobStatus.Pending, string? leaseOwner = null,
        DateTimeOffset? leaseExpiresAt = null, DateTimeOffset? nextAttemptAt = null,
        int attempts = 0, DateTimeOffset? updatedAt = null, Guid? groupKey = null)
    {
        await using var db = NewDb();
        var now = DateTimeOffset.UtcNow;
        var job = new Job
        {
            GroupKey = groupKey ?? Guid.NewGuid(),
            Type = type,
            PayloadJson = "{}",
            Status = status,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = leaseExpiresAt,
            NextAttemptAt = nextAttemptAt,
            Attempts = attempts,
            CreatedAt = now,
            UpdatedAt = updatedAt ?? now,
        };
        db.Jobs.Add(job);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return job;
    }

    internal async Task<Job> LoadAsync(Guid jobId)
    {
        await using var db = NewDb();
        return await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId, TestContext.Current.CancellationToken);
    }

    /// <summary>Starts a host running the block's worker against this database.</summary>
    internal async Task<WorkerHost> StartWorkerAsync(
        Action<IServiceCollection>? configure = null, TimeSpan? lease = null, TimeSpan? poll = null,
        int? maxAttempts = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (lease is { } l)
        {
            values["Jobs:LeaseDuration"] = l.ToString("c", CultureInfo.InvariantCulture);
        }

        if (poll is { } p)
        {
            values["Jobs:SafetyNetPollInterval"] = p.ToString("c", CultureInfo.InvariantCulture);
        }

        if (maxAttempts is { } m)
        {
            values["Jobs:MaxAttempts"] = m.ToString(CultureInfo.InvariantCulture);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDb>(options => options.UseNpgsql(ConnectionString));
        services.AddJobs<TestDb>(new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Jobs"));
        configure?.Invoke(services);

        var host = new WorkerHost(services.BuildServiceProvider());
        await host.StartAsync();
        return host;
    }
}

/// <summary>A DI container with the block registered and its hosted worker running.</summary>
internal sealed class WorkerHost(ServiceProvider provider) : IAsyncDisposable
{
    public IServiceProvider Services => provider;

    public async Task StartAsync()
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StopAsync(CancellationToken.None);
        }

        await provider.DisposeAsync();
    }
}

/// <summary>Counts handler invocations per job id; an optional delay widens the race window.</summary>
internal sealed class CountingHandler(string type, TimeSpan? delay = null) : IJobHandler
{
    public ConcurrentDictionary<Guid, int> Calls { get; } = new();

    public string Type => type;

    public async Task HandleAsync(Job job, CancellationToken cancellationToken)
    {
        Calls.AddOrUpdate(job.Id, 1, (_, n) => n + 1);
        if (delay is { } d)
        {
            await Task.Delay(d, cancellationToken);
        }
    }
}

internal sealed class DelegateHandler(string type, Func<Job, CancellationToken, Task> handle) : IJobHandler
{
    public string Type => type;

    public Task HandleAsync(Job job, CancellationToken cancellationToken) => handle(job, cancellationToken);
}

internal sealed class RecordingObserver(Func<Job, Task>? onChanged = null) : IJobObserver
{
    public ConcurrentQueue<(Guid JobId, string Type, Guid GroupKey, JobStatus Status, string? Detail)> Changes { get; } = new();

    public async Task OnStateChangedAsync(Job job, string? detail, CancellationToken cancellationToken)
    {
        Changes.Enqueue((job.Id, job.Type, job.GroupKey, job.Status, detail));
        if (onChanged is not null)
        {
            await onChanged(job);
        }
    }
}

internal static class Wait
{
    public static async Task UntilAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(20);
        using var cts = new CancellationTokenSource(limit);
        while (!await condition())
        {
            if (cts.IsCancellationRequested)
            {
                Assert.Fail($"Timed out after {limit}: {because}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        }
    }
}

internal sealed class SpyWakeUp : IJobWakeUp
{
    public int Signals { get; private set; }

    public void Signal() => Signals++;
}
