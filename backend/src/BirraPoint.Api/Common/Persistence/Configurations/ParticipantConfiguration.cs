using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.Email).HasMaxLength(320);
        builder.Property(p => p.AcceMemberNumber).HasMaxLength(50);
        builder.Property(p => p.Phone).HasMaxLength(30);

        builder.HasIndex(p => new { p.CompetitionId, p.Email }).IsUnique();

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(p => p.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
