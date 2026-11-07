using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BuildingBlocks.Jobs;

public static class JobsModelBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="Job"/> into the host model for PostgreSQL: <see cref="Job.Type"/> and
    /// <see cref="Job.Status"/> stored as strings, <see cref="Job.PayloadJson"/> as <c>jsonb</c>, and the two
    /// indexes the worker's sweeps use. The worker reads the table and column names from this model, so
    /// <paramref name="configure"/> may rename them, adjust <see cref="Job.Type"/>'s length or add
    /// relationships (e.g. a foreign key on <see cref="Job.GroupKey"/>).
    /// <para>
    /// No optimistic-concurrency token: overlapping revisions are made safe by an atomic claim instead — the
    /// worker selects <c>FOR UPDATE SKIP LOCKED</c>, marks the job Running with a lease, and finishes it only
    /// while it still owns the lease.
    /// </para>
    /// </summary>
    public static ModelBuilder ApplyJobs(this ModelBuilder modelBuilder, Action<EntityTypeBuilder<Job>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Job>(job =>
        {
            job.ToTable("Jobs");
            job.HasKey(j => j.Id);
            job.Property(j => j.Type).HasMaxLength(Job.DefaultTypeMaxLength);
            job.Property(j => j.Status).HasConversion<string>().HasMaxLength(Job.StatusMaxLength);
            job.Property(j => j.PayloadJson).HasColumnType("jsonb");
            job.Property(j => j.LastError).HasMaxLength(Job.LastErrorMaxLength);
            job.Property(j => j.LeaseOwner).HasMaxLength(Job.LeaseOwnerMaxLength);

            // Supports the worker's sweeps: Status == Pending && NextAttemptAt <= now (claim) and
            // Status == Running && LeaseExpiresAt < now (expired-lease recovery).
            job.HasIndex(j => new { j.Status, j.NextAttemptAt });
            job.HasIndex(j => new { j.Status, j.LeaseExpiresAt });

            configure?.Invoke(job);
        });

        return modelBuilder;
    }
}
