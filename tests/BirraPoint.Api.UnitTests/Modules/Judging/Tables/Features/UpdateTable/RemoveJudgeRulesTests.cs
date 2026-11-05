using BirraPoint.Api.Modules.Judging.Features.Tables.Features.UpdateTable;

namespace BirraPoint.Api.UnitTests.Modules.Judging.Tables.Features.UpdateTable;

public sealed class RemoveJudgeRulesTests
{
    [Fact]
    public void ComputeAuditEntityIdIsDeterministicForTheSamePair()
    {
        var tableId = Guid.NewGuid();
        var judgeId = Guid.NewGuid();

        var first = RemoveJudgeRules.ComputeAuditEntityId(tableId, judgeId);
        var second = RemoveJudgeRules.ComputeAuditEntityId(tableId, judgeId);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeAuditEntityIdDiffersWhenTheTableDiffers()
    {
        var judgeId = Guid.NewGuid();

        var forTableA = RemoveJudgeRules.ComputeAuditEntityId(Guid.NewGuid(), judgeId);
        var forTableB = RemoveJudgeRules.ComputeAuditEntityId(Guid.NewGuid(), judgeId);

        Assert.NotEqual(forTableA, forTableB);
    }

    [Fact]
    public void ComputeAuditEntityIdDiffersWhenTheJudgeDiffers()
    {
        var tableId = Guid.NewGuid();

        var forJudgeA = RemoveJudgeRules.ComputeAuditEntityId(tableId, Guid.NewGuid());
        var forJudgeB = RemoveJudgeRules.ComputeAuditEntityId(tableId, Guid.NewGuid());

        Assert.NotEqual(forJudgeA, forJudgeB);
    }

    [Fact]
    public void ComputeAuditEntityIdFitsWithinAuditLogEntityIds50CharacterColumnCap()
    {
        var entityId = RemoveJudgeRules.ComputeAuditEntityId(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(entityId.Length <= 50, $"Expected length <= 50, was {entityId.Length}.");
    }
}
