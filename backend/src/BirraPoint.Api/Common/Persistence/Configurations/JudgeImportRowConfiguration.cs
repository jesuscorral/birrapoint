using BirraPoint.Api.Features.Judges;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class JudgeImportRowConfiguration : IEntityTypeConfiguration<JudgeImportRow>
{
    public void Configure(EntityTypeBuilder<JudgeImportRow> builder)
    {
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.Email).HasMaxLength(320);
        builder.Property(r => r.BjcpRank).HasMaxLength(100);
        builder.Property(r => r.BjcpId).HasMaxLength(50);
        builder.Property(r => r.PreferredCategory).HasMaxLength(200);
        builder.Property(r => r.Preferences).HasMaxLength(2000);
        builder.Property(r => r.ErrorMessage).HasMaxLength(1000);

        builder.HasIndex(r => new { r.JudgeImportBatchId, r.RowNumber }).IsUnique();

        builder.HasOne<JudgeImportBatch>()
            .WithMany(b => b.Rows)
            .HasForeignKey(r => r.JudgeImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
