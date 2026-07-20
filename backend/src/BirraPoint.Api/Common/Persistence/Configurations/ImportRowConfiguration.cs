using BirraPoint.Api.Features.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> builder)
    {
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ParticipantName).HasMaxLength(200);
        builder.Property(r => r.ParticipantEmail).HasMaxLength(320);
        builder.Property(r => r.BeerName).HasMaxLength(200);
        builder.Property(r => r.StyleText).HasMaxLength(200);
        builder.Property(r => r.ResolvedStyleCode).HasMaxLength(20);
        builder.Property(r => r.ErrorMessage).HasMaxLength(1000);
        builder.Property(r => r.CollaboratorsJson).HasColumnType("jsonb");

        builder.HasIndex(r => new { r.ImportBatchId, r.RowNumber }).IsUnique();

        builder.HasOne<ImportBatch>()
            .WithMany(b => b.Rows)
            .HasForeignKey(r => r.ImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
