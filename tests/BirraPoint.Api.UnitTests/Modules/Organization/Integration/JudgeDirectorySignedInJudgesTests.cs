using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Identity;
using BirraPoint.Api.Modules.Organization.Integration;
using BuildingBlocks.Identity;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.UnitTests.Modules.Organization.Integration;

/// <summary>IJudgeDirectory.GetSignedInJudgesAsync maps the caller's resolved judge records (one per
/// competition) to summaries; no database access (the context is never queried).</summary>
public class JudgeDirectorySignedInJudgesTests
{
    private static AppDbContext UnusedDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=unused").Options);

    [Fact]
    public async Task MapsEveryResolvedJudgeRecordToASummary()
    {
        var first = new Judge
        {
            CompetitionId = Guid.NewGuid(),
            Email = "ana@judges.example",
            DisplayName = "Ana",
            BjcpRank = "Certified",
        };
        var second = new Judge { CompetitionId = Guid.NewGuid(), Email = "ana@judges.example", DisplayName = "Ana R." };
        await using var dbContext = UnusedDbContext();
        var directory = new JudgeDirectory(dbContext, new FakeUser(), new FakeResolver([first, second]));

        var judges = await directory.GetSignedInJudgesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new JudgeSummary(first.Id, first.CompetitionId, "ana@judges.example", "Ana", "Certified"),
                new JudgeSummary(second.Id, second.CompetitionId, "ana@judges.example", "Ana R.", null),
            ],
            judges);
    }

    [Fact]
    public async Task ReturnsEmptyWhenTheCallerIsNotAJudge()
    {
        await using var dbContext = UnusedDbContext();
        var directory = new JudgeDirectory(dbContext, new FakeUser(), new FakeResolver([]));

        Assert.Empty(await directory.GetSignedInJudgesAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FakeUser : ICurrentUser
    {
        public string Sub => "judge-sub";

        public string? Email => "ana@judges.example";

        public string? Name => null;

        public string? GivenName => null;

        public string? FamilyName => null;

        public IReadOnlyList<string> Roles => ["JUDGE"];
    }

    private sealed class FakeResolver(IReadOnlyList<Judge> judges) : IJudgeResolver
    {
        public Task<IReadOnlyList<Judge>> ResolveAndBackfillAsync(
            string sub, string? email, string? name, CancellationToken ct = default) => Task.FromResult(judges);
    }
}
