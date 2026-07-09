using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BirraPoint.Api.Common.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations …` at design time; no connection is opened when
/// generating migrations, so the connection string is a placeholder.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=birrapoint-design;Username=postgres;Password=postgres")
            .Options;
        return new AppDbContext(options);
    }
}
