using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class ResultsArchiveConfiguration : IEntityTypeConfiguration<ResultsArchive>
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
