using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Modules.Organization.Persistence;

internal sealed class CompetitionCategoryConfiguration : IEntityTypeConfiguration<CompetitionCategory>
{
    public void Configure(EntityTypeBuilder<CompetitionCategory> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(100);

        builder.HasIndex(c => new { c.CompetitionId, c.Name }).IsUnique();

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(c => c.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
