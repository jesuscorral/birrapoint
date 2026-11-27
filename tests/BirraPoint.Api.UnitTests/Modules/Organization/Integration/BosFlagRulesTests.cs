using BirraPoint.Api.Modules.Organization.Integration;

namespace BirraPoint.Api.UnitTests.Modules.Organization.Integration;

public class BosFlagRulesTests
{
    private static readonly Guid Entry = Guid.NewGuid();
    private static readonly Guid OtherEntry = Guid.NewGuid();
    private static readonly Guid JudgeA = Guid.NewGuid();
    private static readonly Guid JudgeB = Guid.NewGuid();

    private static Dictionary<Guid, IReadOnlySet<Guid>> CoOwners(params (Guid Entry, Guid[] Judges)[] rows) =>
        rows.ToDictionary(r => r.Entry, r => (IReadOnlySet<Guid>)r.Judges.ToHashSet());

    [Fact]
    public void EntryIsFlaggedWhenAnyCoOwnerJudgeIsEngaged()
    {
        var flags = BosFlagRules.Compute([Entry], CoOwners((Entry, [JudgeA, JudgeB])), new HashSet<Guid> { JudgeB });

        Assert.True(flags[Entry]);
    }

    [Fact]
    public void EntryIsClearedWhenNoCoOwnerJudgeIsEngaged()
    {
        var flags = BosFlagRules.Compute([Entry], CoOwners((Entry, [JudgeA, JudgeB])), new HashSet<Guid>());

        Assert.False(flags[Entry]);
    }

    [Fact]
    public void EntryWithoutCoOwnerJudgesIsNeverFlagged()
    {
        var flags = BosFlagRules.Compute([Entry, OtherEntry], CoOwners((OtherEntry, [JudgeA])), new HashSet<Guid> { JudgeA });

        Assert.False(flags[Entry]);
        Assert.True(flags[OtherEntry]);
    }

    [Fact]
    public void DuplicateEntryIdsCollapse()
    {
        var flags = BosFlagRules.Compute([Entry, Entry], CoOwners(), new HashSet<Guid>());

        Assert.Single(flags);
    }
}
