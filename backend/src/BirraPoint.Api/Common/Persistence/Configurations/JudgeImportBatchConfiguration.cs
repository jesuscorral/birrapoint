using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Judges;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class JudgeImportBatchConfiguration : IEntityTypeConfiguration<JudgeImportBatch>
{
    public void Configure(EntityTypeBuilder<JudgeImportBatch> builder)
    {
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);

        // At most one Pending batch per competition — a new upload discards the prior
        // unconsolidated one (contracts/judge-import-file.md §Semantics), independent of the
        // beer-entry import's own single-active-batch rule (ImportBatchConfiguration.cs).
        builder.HasIndex(b => b.CompetitionId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'");

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(b => b.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
