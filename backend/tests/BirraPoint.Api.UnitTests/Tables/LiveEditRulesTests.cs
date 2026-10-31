using BirraPoint.Api.Features.Tables.Features.UpdateTable;

namespace BirraPoint.Api.UnitTests.Tables;

/// <summary>Pure classification of an InEvaluation PUT (FR-039): only a request that keeps the
/// name and sample set and names a subset of the active judges is accepted.</summary>
public sealed class LiveEditRulesTests
{
    private static readonly Guid JudgeA = Guid.NewGuid();
    private static readonly Guid JudgeB = Guid.NewGuid();
    private static readonly Guid JudgeC = Guid.NewGuid();
    private static readonly Guid EntryX = Guid.NewGuid();
    private static readonly Guid EntryY = Guid.NewGuid();

    private static LiveEditClassification Classify(
        string requestName, Guid[] requestSamples, Guid[] requestJudges) =>
        LiveEditRules.Classify("T1", [EntryX, EntryY], [JudgeA, JudgeB], requestName, requestSamples, requestJudges);

    [Fact]
    public void IdenticalRequestIsANoOp()
    {
        var result = Classify("T1", [EntryY, EntryX], [JudgeB, JudgeA]);

        Assert.Equal(LiveEditKind.NoOp, result.Kind);
        Assert.Empty(result.RemovedJudgeIds);
    }

    [Fact]
    public void SubsetOfJudgesIsARemovalListingTheMissingOnes()
    {
        var result = Classify("T1", [EntryX, EntryY], [JudgeA]);

        Assert.Equal(LiveEditKind.Removal, result.Kind);
        Assert.Equal([JudgeB], result.RemovedJudgeIds);
    }

    [Fact]
    public void RemovingEveryJudgeIsARemoval()
    {
        var result = Classify("T1", [EntryX, EntryY], []);

        Assert.Equal(LiveEditKind.Removal, result.Kind);
        Assert.Equal(2, result.RemovedJudgeIds.Count);
    }

    [Fact]
    public void RenameIsForbidden() =>
        Assert.Equal(LiveEditKind.Forbidden, Classify("T2", [EntryX, EntryY], [JudgeA]).Kind);

    [Fact]
    public void NameComparisonIsExact() =>
        Assert.Equal(LiveEditKind.Forbidden, Classify("t1", [EntryX, EntryY], [JudgeA, JudgeB]).Kind);

    [Fact]
    public void DroppingASampleIsForbidden() =>
        Assert.Equal(LiveEditKind.Forbidden, Classify("T1", [EntryX], [JudgeA]).Kind);

    [Fact]
    public void AddingASampleIsForbidden() =>
        Assert.Equal(LiveEditKind.Forbidden, Classify("T1", [EntryX, EntryY, Guid.NewGuid()], [JudgeA]).Kind);

    [Fact]
    public void AddingAJudgeIsForbidden() =>
        Assert.Equal(LiveEditKind.Forbidden, Classify("T1", [EntryX, EntryY], [JudgeA, JudgeB, JudgeC]).Kind);

    [Fact]
    public void SwappingAJudgeIsForbidden() =>
        Assert.Equal(LiveEditKind.Forbidden, Classify("T1", [EntryX, EntryY], [JudgeA, JudgeC]).Kind);
}
