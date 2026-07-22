using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class GeneratedScoreSheetConfiguration : IEntityTypeConfiguration<GeneratedScoreSheet>
{
    public void Configure(EntityTypeBuilder<GeneratedScoreSheet> builder)
    {
        // Upsert-by-BeerEntryId lookup key (a job retry must not create duplicate rows).
        builder.HasIndex(s => s.BeerEntryId).IsUnique();

        builder.HasOne<BeerEntry>()
            .WithMany()
            .HasForeignKey(s => s.BeerEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
