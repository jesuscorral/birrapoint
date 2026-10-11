using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Import;

/// <summary>
/// Slice-owned staging area for one `.xlsx` upload (contracts/import-file.md); at most one
/// <see cref="ImportBatchStatus.Pending"/> batch per competition — a new upload discards the
/// prior unconsolidated one.
/// </summary>
internal class ImportBatch : Entity
{
    public Guid CompetitionId { get; set; }

    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Pending;

    public ICollection<ImportRow> Rows { get; set; } = [];
}

internal enum ImportBatchStatus
{
    Pending,
    Consolidated,
}
