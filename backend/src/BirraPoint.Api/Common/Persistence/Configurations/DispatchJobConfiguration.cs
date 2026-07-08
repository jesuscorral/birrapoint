using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class DispatchJobConfiguration : IEntityTypeConfiguration<DispatchJob>
{
    public void Configure(EntityTypeBuilder<DispatchJob> builder)
    {
        builder.Property(j => j.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.PayloadJson).HasColumnType("jsonb");
        builder.Property(j => j.LastError).HasMaxLength(2000);

        builder.HasOne<Competition>()
            .WithMany()
            .HasForeignKey(j => j.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
