using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class CompetitionCategoryStyleConfiguration : IEntityTypeConfiguration<CompetitionCategoryStyle>
{
    public void Configure(EntityTypeBuilder<CompetitionCategoryStyle> builder)
    {
        builder.HasKey(s => new { s.CompetitionCategoryId, s.StyleCode });

        builder.Property(s => s.StyleCode).HasMaxLength(20);

        builder.HasOne<CompetitionCategory>()
            .WithMany(c => c.Styles)
            .HasForeignKey(s => s.CompetitionCategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // The catalog is a read-only seed — it must never cascade-delete.
        builder.HasOne<BjcpStyle>()
            .WithMany()
            .HasForeignKey(s => s.StyleCode)
            .OnDelete(DeleteBehavior.Restrict);

        // Denormalized copy of the owning category's CompetitionId (see CompetitionCategoryStyle
        // XML doc) — NoAction because the CompetitionCategory cascade path above already handles
        // cleanup transitively; two independent cascade paths converging on Competitions from the
        // same descendant table would conflict.
        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(s => s.CompetitionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(s => new { s.CompetitionId, s.StyleCode }).IsUnique();
    }
}
