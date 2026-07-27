using BirraPoint.Api.Features.Tables;

namespace BirraPoint.Api.UnitTests.Tables;

/// <summary>
/// T084: RemoveJudgeCommandHandler itself has no meaningful pure logic to isolate — its only
/// branching is the ownership/active-membership lookup chain, which needs a real transaction
/// against Postgres (Testcontainers) to exercise honestly; that end-to-end behavior (RemovedAt set
/// with no hard delete, an already-submitted evaluation staying valid, the pre-existing
/// JudgeTableAccess guard actually engaging once RemovedAt is set, and the hub group ejection via
/// IEventPublisher) is covered by RemoveJudgeApiTests (T085). The one pure unit this slice does
/// have is RemoveJudgeRules.ComputeAuditEntityId — TableJudge's composite (TastingTableId, JudgeId)
/// PK has no single Guid id to key the audit row on, so this hashes the pair down to fit
/// AuditLog.EntityId's 50-char cap; that hashing is exercised directly here.
/// </summary>
public sealed class RemoveJudgeTests
{
    [Fact]
    public void ComputeAuditEntityId_is_deterministic_for_the_same_pair()
    {
        var tableId = Guid.NewGuid();
        var judgeId = Guid.NewGuid();

        var first = RemoveJudgeRules.ComputeAuditEntityId(tableId, judgeId);
        var second = RemoveJudgeRules.ComputeAuditEntityId(tableId, judgeId);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeAuditEntityId_differs_when_the_table_differs()
    {
        var judgeId = Guid.NewGuid();

        var forTableA = RemoveJudgeRules.ComputeAuditEntityId(Guid.NewGuid(), judgeId);
        var forTableB = RemoveJudgeRules.ComputeAuditEntityId(Guid.NewGuid(), judgeId);

        Assert.NotEqual(forTableA, forTableB);
    }

    [Fact]
    public void ComputeAuditEntityId_differs_when_the_judge_differs()
    {
        var tableId = Guid.NewGuid();

        var forJudgeA = RemoveJudgeRules.ComputeAuditEntityId(tableId, Guid.NewGuid());
        var forJudgeB = RemoveJudgeRules.ComputeAuditEntityId(tableId, Guid.NewGuid());

        Assert.NotEqual(forJudgeA, forJudgeB);
    }

    [Fact]
    public void ComputeAuditEntityId_fits_within_AuditLog_EntityIds_50_character_column_cap()
    {
        var entityId = RemoveJudgeRules.ComputeAuditEntityId(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(entityId.Length <= 50, $"Expected length <= 50, was {entityId.Length}.");
    }
}
