using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class TableJudgeConfiguration : IEntityTypeConfiguration<TableJudge>
{
    public void Configure(EntityTypeBuilder<TableJudge> builder)
    {
        builder.HasKey(tj => new { tj.TastingTableId, tj.JudgeId });

        builder.HasOne<Judge>()
            .WithMany()
            .HasForeignKey(tj => tj.JudgeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
