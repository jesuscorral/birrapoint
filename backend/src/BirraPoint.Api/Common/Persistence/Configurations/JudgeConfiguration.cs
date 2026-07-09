using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class JudgeConfiguration : IEntityTypeConfiguration<Judge>
{
    public void Configure(EntityTypeBuilder<Judge> builder)
    {
        builder.Property(j => j.Email).HasMaxLength(320);
        builder.Property(j => j.KeycloakUserId).HasMaxLength(255);
        builder.Property(j => j.DisplayName).HasMaxLength(200);

        builder.HasIndex(j => new { j.CompetitionId, j.Email }).IsUnique();

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(j => j.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
