using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BirraPoint.Api.Common.Persistence.Configurations;

public sealed class CompetitionConfiguration : IEntityTypeConfiguration<Competition>
{
    public void Configure(EntityTypeBuilder<Competition> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.Venue).HasMaxLength(200);
        builder.Property(c => c.Description).HasMaxLength(2000);
        builder.Property(c => c.LogoUrl).HasMaxLength(500);
        builder.Property(c => c.CreatedByUserId).HasMaxLength(255);
        builder.Property(c => c.State).HasConversion<string>().HasMaxLength(20);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Competitions_EndDate", "\"EndDate\" >= \"StartDate\"");
            t.HasCheckConstraint(
                "CK_Competitions_RegistrationWindow",
                "\"StartRegistration\" IS NULL OR \"EndRegistration\" IS NULL OR \"EndRegistration\" >= \"StartRegistration\"");
            t.HasCheckConstraint("CK_Competitions_EntryLimit", "\"EntryLimit\" IS NULL OR \"EntryLimit\" > 0");
        });
    }
}
