using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Integration;
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
        var directory = new JudgeDirectory(dbContext, new FakeUser([first, second]));

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
        var directory = new JudgeDirectory(dbContext, new FakeUser([]));

        Assert.Empty(await directory.GetSignedInJudgesAsync(TestContext.Current.CancellationToken));
    }

    private sealed class FakeUser(IReadOnlyList<Judge> judges) : ICurrentUser
    {
        public string Sub => "judge-sub";

        public string? Email => "ana@judges.example";

        public string? Name => null;

        public string? GivenName => null;

        public string? FamilyName => null;

        public IReadOnlyList<string> Roles => ["JUDGE"];

        public Task<IReadOnlyList<Judge>> GetJudgeRecordsAsync(CancellationToken ct = default) => Task.FromResult(judges);

        public Task<Organizer> GetOrganizerAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
