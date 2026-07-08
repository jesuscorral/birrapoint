using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class EvaluationConfiguration : IEntityTypeConfiguration<Evaluation>
{
    public void Configure(EntityTypeBuilder<Evaluation> builder)
    {
        builder.Property(e => e.AromaComment).HasMaxLength(2000);
        builder.Property(e => e.AppearanceComment).HasMaxLength(2000);
        builder.Property(e => e.FlavorComment).HasMaxLength(2000);
        builder.Property(e => e.MouthfeelComment).HasMaxLength(2000);
        builder.Property(e => e.OverallComment).HasMaxLength(2000);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        // FR-024: the total is computed by the database, never client-supplied.
        builder.Property(e => e.Total)
            .HasComputedColumnSql(
                "\"AromaScore\" + \"AppearanceScore\" + \"FlavorScore\" + \"MouthfeelScore\" + \"OverallScore\"",
                stored: true);

        // FR-029/R-07: idempotency backstop — never UPSERT against it.
        builder.HasIndex(e => new { e.JudgeId, e.BeerEntryId }).IsUnique();

        builder.HasOne<TastingTable>()
            .WithMany()
            .HasForeignKey(e => e.TastingTableId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Judge>()
            .WithMany()
            .HasForeignKey(e => e.JudgeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<BeerEntry>()
            .WithMany()
            .HasForeignKey(e => e.BeerEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
