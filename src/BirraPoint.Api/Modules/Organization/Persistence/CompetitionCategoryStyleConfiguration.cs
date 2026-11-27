using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Modules.Organization.Persistence;

internal sealed class CompetitionCategoryStyleConfiguration : IEntityTypeConfiguration<CompetitionCategoryStyle>
{
    public void Configure(EntityTypeBuilder<CompetitionCategoryStyle> builder)
    {
        builder.HasKey(s => new { s.CompetitionCategoryId, s.StyleCode });

        builder.Property(s => s.StyleCode).HasMaxLength(20);

        builder.HasOne<CompetitionCategory>()
            .WithMany(c => c.Styles)
            .HasForeignKey(s => s.CompetitionCategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Denormalized copy of the owning category's CompetitionId (see CompetitionCategoryStyle
        // XML doc) — NoAction because the CompetitionCategory cascade path above already handles
        // cleanup transitively; two independent cascade paths converging on Competitions from the
        // same descendant table would conflict.
        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(s => s.CompetitionId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(s => new { s.CompetitionId, s.StyleCode }).IsUnique();

        // Index kept from the dropped foreign key: lookups by the plain id (FR-093).
        builder.HasIndex(s => s.StyleCode);
    }
}
