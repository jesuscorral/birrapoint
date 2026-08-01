using BirraPoint.Api.Features.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> builder)
    {
        // 25, not 20: "CategoryStyleMismatch" (FR-053) is 21 characters — longest current member.
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(25);
        builder.Property(r => r.ParticipantName).HasMaxLength(200);
        builder.Property(r => r.ParticipantEmail).HasMaxLength(320);
        builder.Property(r => r.AcceMemberNumberText).HasMaxLength(50);
        builder.Property(r => r.Phone).HasMaxLength(30);
        builder.Property(r => r.CategoryText).HasMaxLength(200);
        builder.Property(r => r.StyleText).HasMaxLength(200);
        builder.Property(r => r.ResolvedStyleCode).HasMaxLength(20);
        builder.Property(r => r.AbvPercent).HasColumnType("decimal(4,2)");
        builder.Property(r => r.Malts).HasMaxLength(1000);
        builder.Property(r => r.Hops).HasMaxLength(1000);
        builder.Property(r => r.Yeast).HasMaxLength(1000);
        builder.Property(r => r.OtherIngredients).HasMaxLength(1000);
        builder.Property(r => r.EntryInstructions).HasMaxLength(1000);
        builder.Property(r => r.BeerName).HasMaxLength(200);
        builder.Property(r => r.ErrorMessage).HasMaxLength(1000);

        builder.HasIndex(r => new { r.ImportBatchId, r.RowNumber }).IsUnique();

        builder.HasOne<ImportBatch>()
            .WithMany(b => b.Rows)
            .HasForeignKey(r => r.ImportBatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
