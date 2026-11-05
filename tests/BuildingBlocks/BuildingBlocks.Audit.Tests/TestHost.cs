using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BuildingBlocks.Audit.Tests;

/// <summary>A host-agnostic DbContext: the block only needs <see cref="AuditModelBuilderExtensions.ApplyAuditLog"/>.</summary>
internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyAuditLog(configure: audit => audit.Property(a => a.DataJson).HasColumnType("jsonb"));
}

/// <summary>One PostgreSQL 16 container per test class; schema created from the test model.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    internal DbContextOptions<TestDbContext> Options { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Options = new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(_container.GetConnectionString()).Options;

        await using var db = new TestDbContext(Options);
        await db.Database.EnsureCreatedAsync();
    }

    ValueTask IAsyncDisposable.DisposeAsync() => _container.DisposeAsync();
}

internal sealed class FixedActor(string actorUserId) : IAuditActorAccessor
{
    public string ActorUserId { get; } = actorUserId;
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
