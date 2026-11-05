using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Domain;

namespace BirraPoint.Api.UnitTests.Modules.Judging.Tables;

public class OwnedCompetitionLookupTests
{
    [Fact]
    public async Task RepeatedLookupsOfTheSameCompetitionHitTheContractOnce()
    {
        var access = new CountingAccess();
        var lookup = new OwnedCompetitionLookup(access, new FakeUser());
        var id = Guid.NewGuid();

        var first = await lookup.GetOwnedAsync(id, CancellationToken.None);
        var second = await lookup.GetOwnedAsync(id, CancellationToken.None);
        await lookup.GetOwnedAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(2, access.OwnedCalls);
    }

    private sealed class CountingAccess : ICompetitionAccess
    {
        public int OwnedCalls { get; private set; }

        public Task<CompetitionSnapshot?> GetOwnedAsync(Guid competitionId, string ownerUserId, CancellationToken cancellationToken = default)
        {
            OwnedCalls++;
            return Task.FromResult<CompetitionSnapshot?>(new CompetitionSnapshot(competitionId, "c", CompetitionState.Active));
        }

        public Task<CompetitionSnapshot?> GetAsync(Guid competitionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, CompetitionState>> GetStatesAsync(IReadOnlyCollection<Guid> competitionIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUser : ICurrentUser
    {
        public string Sub => "owner";

        public string? Email => null;

        public string? Name => null;

        public string? GivenName => null;

        public string? FamilyName => null;

        public IReadOnlyList<string> Roles => [];

        public Task<IReadOnlyList<Judge>> GetJudgeRecordsAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Organizer> GetOrganizerAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
