using BirraPoint.Api.Modules.Judging.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Modules.Judging.Persistence;

internal sealed class DiscrepancyAlertConfiguration : IEntityTypeConfiguration<DiscrepancyAlert>
{
    public void Configure(EntityTypeBuilder<DiscrepancyAlert> builder)
    {
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        // At most one open alert per (table, entry) — partial unique index (data-model.md).
        builder.HasIndex(a => new { a.TastingTableId, a.BeerEntryId })
            .IsUnique()
            .HasFilter("\"Status\" = 'Open'");

        // Index kept from the dropped foreign key: lookups by the plain id (FR-093).
        builder.HasIndex(a => a.BeerEntryId);

        builder.HasOne<TastingTable>()
            .WithMany()
            .HasForeignKey(a => a.TastingTableId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
