using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class DiscrepancyAlertConfiguration : IEntityTypeConfiguration<DiscrepancyAlert>
{
    public void Configure(EntityTypeBuilder<DiscrepancyAlert> builder)
    {
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        // At most one open alert per (table, entry) — partial unique index (data-model.md).
        builder.HasIndex(a => new { a.TastingTableId, a.BeerEntryId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Open'");

        builder.HasOne<TastingTable>()
            .WithMany()
            .HasForeignKey(a => a.TastingTableId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BeerEntry>()
            .WithMany()
            .HasForeignKey(a => a.BeerEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
