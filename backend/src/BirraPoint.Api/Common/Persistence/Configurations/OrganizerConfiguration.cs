using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class OrganizerConfiguration : IEntityTypeConfiguration<Organizer>
{
    public void Configure(EntityTypeBuilder<Organizer> builder)
    {
        builder.Property(o => o.KeycloakUserId).HasMaxLength(255);
        builder.Property(o => o.Email).HasMaxLength(320);
        builder.Property(o => o.FirstName).HasMaxLength(200);
        builder.Property(o => o.LastName).HasMaxLength(200);

        builder.HasIndex(o => o.KeycloakUserId).IsUnique();
        builder.HasIndex(o => o.Email).IsUnique();
    }
}
