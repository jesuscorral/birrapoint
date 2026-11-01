using System.Security.Cryptography;
using System.Text;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.Features.UpdateTable;

internal enum LiveEditKind
{
    /// <summary>Request equals the current state; nothing to do.</summary>
    NoOp,

    /// <summary>Request only drops currently-active judges.</summary>
    Removal,

    /// <summary>Request changes anything else; rejected (409) with nothing persisted.</summary>
    Forbidden,
}

internal readonly record struct LiveEditClassification(LiveEditKind Kind, IReadOnlyList<Guid> RemovedJudgeIds);

internal static class LiveEditRules
{
    public static LiveEditClassification Classify(
        string currentName,
        IReadOnlyCollection<Guid> currentSampleIds,
        IReadOnlyCollection<Guid> currentActiveJudgeIds,
        string requestName,
        IReadOnlyCollection<Guid> requestSampleIds,
        IReadOnlyCollection<Guid> requestJudgeIds)
    {
        var requestedJudges = requestJudgeIds.ToHashSet();

        if (!string.Equals(currentName, requestName, StringComparison.Ordinal)
            || !currentSampleIds.ToHashSet().SetEquals(requestSampleIds)
            || !requestedJudges.IsSubsetOf(currentActiveJudgeIds))
        {
            return new LiveEditClassification(LiveEditKind.Forbidden, []);
        }

        var removed = currentActiveJudgeIds.Where(id => !requestedJudges.Contains(id)).ToList();
        return new LiveEditClassification(removed.Count == 0 ? LiveEditKind.NoOp : LiveEditKind.Removal, removed);
    }
}

/// <summary>Pure helper for the live judge removal audit trail.</summary>
internal static class RemoveJudgeRules
{
    /// <summary>Composite (TastingTableId, JudgeId) PK has no single Guid id to key the audit row
    /// on, and AuditLog.EntityId is capped at 50 chars (AuditLogConfiguration.cs) — too short for
    /// both GUIDs verbatim, so a SHA-256 hash of "{tableId}:{judgeId}" (same hex-digest idiom as
    /// BjcpStyleCatalogLoader), truncated to 40 hex chars (160 bits) to stay well within the cap,
    /// gives a deterministic, effectively-unique key for this pair.</summary>
    public static string ComputeAuditEntityId(Guid tableId, Guid judgeId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tableId}:{judgeId}")))[..40];
}
