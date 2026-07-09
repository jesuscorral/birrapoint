using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class TastingTableConfiguration : IEntityTypeConfiguration<TastingTable>
{
    public void Configure(EntityTypeBuilder<TastingTable> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(100);
        builder.Property(t => t.State).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(t => new { t.CompetitionId, t.Name }).IsUnique();

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(t => t.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // NO ACTION so a competition cascade (judges + tables in one statement) can't trip it.
        builder.HasOne<Judge>()
            .WithMany()
            .HasForeignKey(t => t.OrderFixedByJudgeId)
            .OnDelete(DeleteBehavior.ClientSetNull);

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
