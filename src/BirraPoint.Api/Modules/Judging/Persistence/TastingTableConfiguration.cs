using BirraPoint.Api.Modules.Judging.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Modules.Judging.Persistence;

internal sealed class TastingTableConfiguration : IEntityTypeConfiguration<TastingTable>
{
    public void Configure(EntityTypeBuilder<TastingTable> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(100);
        builder.Property(t => t.State).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(t => new { t.CompetitionId, t.Name }).IsUnique();

        // Index kept from the dropped foreign key: lookups by the plain id (FR-093).
        builder.HasIndex(t => t.OrderFixedByJudgeId);

        builder.HasMany(t => t.Judges)
            .WithOne()
            .HasForeignKey(tj => tj.TastingTableId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Samples)
            .WithOne()
            .HasForeignKey(ts => ts.TastingTableId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
