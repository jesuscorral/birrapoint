using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Modules.Organization.Features.Import;

/// <summary>
/// Slice-owned staging area for one `.xlsx` upload (contracts/import-file.md); at most one
/// <see cref="BeerImportBatchStatus.Pending"/> batch per competition — a new upload discards the
/// prior unconsolidated one.
/// </summary>
internal class BeerImportBatch : Entity
{
    public Guid CompetitionId { get; set; }

    public BeerImportBatchStatus Status { get; set; } = BeerImportBatchStatus.Pending;

    public ICollection<BeerImportRow> Rows { get; set; } = [];
}

internal enum BeerImportBatchStatus
{
    Pending,
    Consolidated,
}
