using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Judges;

/// <summary>
/// Slice-owned staging area for one judge-roster `.xlsx` upload (contracts/judge-import-file.md);
/// at most one <see cref="JudgeImportBatchStatus.Pending"/> batch per competition — a new upload
/// discards the prior unconsolidated one. Mirrors Features/Import/ImportBatch.cs exactly
/// (US14/R-20), independent of the beer-entry import's own single-active-batch rule.
/// </summary>
public class JudgeImportBatch : Entity
{
    public Guid CompetitionId { get; set; }

    public JudgeImportBatchStatus Status { get; set; } = JudgeImportBatchStatus.Pending;

    public ICollection<JudgeImportRow> Rows { get; set; } = [];
}

public enum JudgeImportBatchStatus
{
    Pending,
    Consolidated,
}
