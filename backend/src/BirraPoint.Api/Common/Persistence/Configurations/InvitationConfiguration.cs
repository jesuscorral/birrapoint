using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.LastError).HasMaxLength(2000);

        builder.HasOne<Judge>()
            .WithMany()
            .HasForeignKey(i => i.JudgeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
