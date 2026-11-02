using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Modules.Judging.Persistence;

internal sealed class TableJudgeConfiguration : IEntityTypeConfiguration<TableJudge>
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
