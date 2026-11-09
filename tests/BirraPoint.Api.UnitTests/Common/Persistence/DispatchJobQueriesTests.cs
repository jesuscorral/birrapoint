using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.UnitTests.Common.Persistence;

/// <summary>The job table keeps its original names (DispatchJobs / CompetitionId) after moving into the jobs
/// block, and type filters translate to plain SQL equality.</summary>
public sealed class DispatchJobQueriesTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options);

    [Fact]
    public void OfDispatchTypeTranslatesToAnEqualityOnTheTypeColumn()
    {
        using var db = NewDb();

        var sql = db.DispatchJobs.OfDispatchType(DispatchJobType.SendResultEmail).ToQueryString();

        Assert.Contains("FROM jobs.\"DispatchJobs\"", sql, StringComparison.Ordinal);
        Assert.Contains(".\"Type\" = @type", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupKeyIsStoredInTheCompetitionIdColumn()
    {
        using var db = NewDb();

        var sql = db.DispatchJobs.Where(j => j.GroupKey == Guid.Empty).ToQueryString();

        Assert.Contains("\"CompetitionId\"", sql, StringComparison.Ordinal);
    }
}
