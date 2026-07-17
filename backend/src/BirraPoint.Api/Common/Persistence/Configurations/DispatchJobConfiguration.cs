using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

/// <summary>
/// No optimistic-concurrency token: DispatchWorker's claim-by-query (Status == Pending) assumes a
/// single worker instance (R-06, MVP hosting one live event). Scaling to multiple API replicas
/// would let two workers double-process the same job — add a concurrency token or an atomic
/// claim (`UPDATE ... WHERE Status = 'Pending'` with a rows-affected check) before that happens.
/// </summary>
public sealed class DispatchJobConfiguration : IEntityTypeConfiguration<DispatchJob>
{
    public void Configure(EntityTypeBuilder<DispatchJob> builder)
    {
        builder.Property(j => j.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.PayloadJson).HasColumnType("jsonb");
        builder.Property(j => j.LastError).HasMaxLength(2000);

        // Supports DispatchWorker's two hot-path sweeps: Status == Running (resume) and
        // Status == Pending && NextAttemptAt <= now (dispatch), both ordered by CreatedAt.
        builder.HasIndex(j => new { j.Status, j.NextAttemptAt });

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(j => j.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
