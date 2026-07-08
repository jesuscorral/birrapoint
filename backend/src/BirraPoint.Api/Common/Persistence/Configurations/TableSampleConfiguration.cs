using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class TableSampleConfiguration : IEntityTypeConfiguration<TableSample>
{
    public void Configure(EntityTypeBuilder<TableSample> builder)
    {
        builder.HasKey(ts => new { ts.TastingTableId, ts.BeerEntryId });

        // An entry belongs to at most one table (data-model.md).
        builder.HasIndex(ts => ts.BeerEntryId).IsUnique();

        // 1..M once the order is fixed; multiple NULLs are allowed before that.
        builder.HasIndex(ts => new { ts.TastingTableId, ts.SequenceOrder }).IsUnique();

        builder.HasOne<BeerEntry>()
            .WithMany()
            .HasForeignKey(ts => ts.BeerEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
