using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Publication.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Modules.Publication.Persistence;

internal sealed class ResultsArchiveConfiguration : IEntityTypeConfiguration<ResultsArchive>
{
    public void Configure(EntityTypeBuilder<ResultsArchive> builder)
    {
        // Upsert-by-CompetitionId lookup key (a job retry must not create duplicate rows).
        builder.HasIndex(a => a.CompetitionId).IsUnique();

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(a => a.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
