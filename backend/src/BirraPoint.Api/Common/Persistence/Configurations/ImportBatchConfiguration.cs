using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);

        // At most one Pending batch per competition — a new upload discards the prior
        // unconsolidated one (contracts/import-file.md §Semantics).
        builder.HasIndex(b => b.CompetitionId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'");

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(b => b.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
