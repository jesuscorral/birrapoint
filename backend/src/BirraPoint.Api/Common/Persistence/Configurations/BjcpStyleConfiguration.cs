using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class BjcpStyleConfiguration : IEntityTypeConfiguration<BjcpStyle>
{
    public void Configure(EntityTypeBuilder<BjcpStyle> builder)
    {
        builder.HasKey(s => s.Code);
        builder.Property(s => s.Code).HasMaxLength(5).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.Property(s => s.CategoryNumber).HasMaxLength(3);
        builder.Property(s => s.CategoryName).HasMaxLength(100);
    }
}
