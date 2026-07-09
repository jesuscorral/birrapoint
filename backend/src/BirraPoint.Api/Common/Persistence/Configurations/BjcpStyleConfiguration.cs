using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class BjcpStyleConfiguration : IEntityTypeConfiguration<BjcpStyle>
{
    public void Configure(EntityTypeBuilder<BjcpStyle> builder)
    {
        builder.HasKey(s => s.Code);
        builder.Property(s => s.Code).HasMaxLength(20).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.Property(s => s.CategoryNumber).HasMaxLength(3);
        builder.Property(s => s.CategoryName).HasMaxLength(100);

        builder.Property(s => s.OGLow).HasColumnType("decimal(4,3)");
        builder.Property(s => s.OGHigh).HasColumnType("decimal(4,3)");
        builder.Property(s => s.FGLow).HasColumnType("decimal(4,3)");
        builder.Property(s => s.FGHigh).HasColumnType("decimal(4,3)");
        builder.Property(s => s.SRMLow).HasColumnType("decimal(5,1)");
        builder.Property(s => s.SRMHigh).HasColumnType("decimal(5,1)");
        builder.Property(s => s.ABVLow).HasColumnType("decimal(4,1)");
        builder.Property(s => s.ABVHigh).HasColumnType("decimal(4,1)");

        builder.Property(s => s.DescriptionJson).HasColumnType("jsonb");
    }
}
