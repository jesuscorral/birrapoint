using BirraPoint.Api.Modules.Judging.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Modules.Judging.Persistence;

internal sealed class TableJudgeConfiguration : IEntityTypeConfiguration<TableJudge>
{
    public void Configure(EntityTypeBuilder<TableJudge> builder)
    {
        builder.HasKey(tj => new { tj.TastingTableId, tj.JudgeId });

        // Index kept from the dropped foreign key: lookups by the plain id (FR-093).
        builder.HasIndex(tj => tj.JudgeId);
    }
}
