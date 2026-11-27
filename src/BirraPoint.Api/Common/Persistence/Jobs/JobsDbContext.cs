using BuildingBlocks.Jobs;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Common.Persistence.Jobs;

/// <summary>The <c>jobs</c> schema: the background job queue of the BuildingBlocks.Jobs block (R-06, ADR-0037), kept on
/// the original <c>DispatchJobs</c> table. It lives in the host because the table, column and migration names are host
/// decisions; the block stays free of BirraPoint (ADR-0035).</summary>
internal sealed class JobsDbContext(DbContextOptions<JobsDbContext> options, SharedTransaction? sharedTransaction = null)
    : BirraPointDbContext(options, DbSchemas.Jobs, sharedTransaction)
{
    public DbSet<Job> DispatchJobs => Set<Job>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // GroupKey is the owning competition. Plain id, no foreign key into the organization schema (FR-093).
        modelBuilder.ApplyJobs(job =>
        {
            job.ToTable("DispatchJobs", DbSchemas.Jobs);
            job.Property(j => j.GroupKey).HasColumnName("CompetitionId");
            job.Property(j => j.Type).HasMaxLength(30);

            // Lookups by competition (status, retry, in-flight checks); kept from the dropped foreign key (FR-093).
            job.HasIndex(j => j.GroupKey);
        });
    }
}
