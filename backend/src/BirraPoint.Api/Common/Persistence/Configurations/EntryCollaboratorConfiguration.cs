using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class EntryCollaboratorConfiguration : IEntityTypeConfiguration<EntryCollaborator>
{
    public void Configure(EntityTypeBuilder<EntryCollaborator> builder)
    {
        builder.HasKey(c => new { c.BeerEntryId, c.Email });
        builder.Property(c => c.Email).HasMaxLength(320);
    }
}
