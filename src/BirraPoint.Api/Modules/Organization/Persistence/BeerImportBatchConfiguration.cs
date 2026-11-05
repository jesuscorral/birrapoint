using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Features.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Modules.Organization.Persistence;

internal sealed class BeerImportBatchConfiguration : IEntityTypeConfiguration<BeerImportBatch>
{
    public void Configure(EntityTypeBuilder<BeerImportBatch> builder)
    {
        // Table names predate the Beer prefix on the CLR types; ToTable keeps the schema unchanged.
        builder.ToTable("ImportBatches");

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
