using BirraPoint.Common.Domain;
using BuildingBlocks.Jobs;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Common.Persistence;

/// <summary>
/// Base of the six data contexts (one per module, plus Jobs and Audit; ADR-0043). Each context maps exactly one
/// PostgreSQL schema, keeps its migration history in that schema (FR-092, FR-097) and stamps <c>CreatedAt</c>/
/// <c>UpdatedAt</c> centrally. Contexts of one DI scope share a connection and a unit of work through
/// <see cref="SharedTransaction"/> (FR-096).
/// </summary>
public abstract class BirraPointDbContext : DbContext
{
    protected BirraPointDbContext(DbContextOptions options, string schema, SharedTransaction? sharedTransaction)
        : base(options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        Schema = schema;
        sharedTransaction?.Register(this);
    }

    /// <summary>The schema every table of this context lives in.</summary>
    public string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
    }

    /// <summary>Applies only the entity configurations declared in <paramref name="configurationNamespace"/>, so a
    /// configuration of another module can never pull a foreign entity into this model (FR-099). Configurations are looked up in the assembly of the concrete context, which is the module's own.</summary>
    protected void ApplyConfigurationsOf(ModelBuilder modelBuilder, string configurationNamespace)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(
            GetType().Assembly,
            type => string.Equals(type.Namespace, configurationNamespace, StringComparison.Ordinal));
    }

    /// <summary>The migration history sits in the context's own schema (FR-097). Added here so every construction
    /// site (host, design-time factory, startup migration, test fixtures) gets it.</summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.UseNpgsql(npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Schema));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>CreatedAt/UpdatedAt are stamped centrally (data-model.md) — handlers never set them.
    /// <see cref="Job"/> belongs to the jobs block, which stamps its own writes; host edits to it (e.g. a
    /// manual retry) are stamped here so UpdatedAt keeps moving.</summary>
    private void StampTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Job>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ITimestamped>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
