using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

/// <summary>
/// No optimistic-concurrency token: overlapping API revisions (ACA rollouts) are made safe by an
/// atomic claim instead (T129) — DispatchWorker selects `FOR UPDATE SKIP LOCKED`, marks the job
/// Running with LeaseOwner/LeaseExpiresAt, and finishes it only `WHERE LeaseOwner = me`. Running
/// jobs whose lease expired (crashed worker) are recovered through the normal retry path.
/// </summary>
public sealed class DispatchJobConfiguration : IEntityTypeConfiguration<DispatchJob>
{
    public void Configure(EntityTypeBuilder<DispatchJob> builder)
    {
        builder.Property(j => j.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.PayloadJson).HasColumnType("jsonb");
        builder.Property(j => j.LastError).HasMaxLength(2000);

        builder.Property(j => j.LeaseOwner).HasMaxLength(200);

        // Supports DispatchWorker's claim sweeps: Status == Pending && NextAttemptAt <= now
        // (dispatch) and Status == Running && LeaseExpiresAt < now (expired-lease recovery).
        builder.HasIndex(j => new { j.Status, j.NextAttemptAt });
        builder.HasIndex(j => new { j.Status, j.LeaseExpiresAt });

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(j => j.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
