using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class BeerEntryConfiguration : IEntityTypeConfiguration<BeerEntry>
{
    public void Configure(EntityTypeBuilder<BeerEntry> builder)
    {
        builder.Property(e => e.BeerName).HasMaxLength(200);
        builder.Property(e => e.StyleCode).HasMaxLength(20);
        builder.Property(e => e.BlindCode).HasMaxLength(10);

        builder.HasIndex(e => new { e.CompetitionId, e.BlindCode }).IsUnique();

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(e => e.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Participant>()
            .WithMany()
            .HasForeignKey(e => e.ParticipantId)
            .OnDelete(DeleteBehavior.Cascade);

        // The catalog is a read-only seed — it must never cascade into entries.
        builder.HasOne<BjcpStyle>()
            .WithMany()
            .HasForeignKey(e => e.StyleCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Collaborators)
            .WithOne()
            .HasForeignKey(c => c.BeerEntryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
