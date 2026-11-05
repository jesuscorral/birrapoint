using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Common.Jobs;

/// <summary>
/// Extensibility point for a <see cref="DispatchJobType"/>: implementations are auto-discovered
/// by <see cref="DispatchWorker"/> via <c>IEnumerable&lt;IDispatchJobHandler&gt;</c>. Must be
/// idempotent (R-06) — <see cref="DispatchWorker"/> may invoke the same job more than once after
/// a crash mid-processing. The first handlers are SendInvitation and
/// GeneratePdfs/BundleZip/SendResultEmail.
/// </summary>
internal interface IDispatchJobHandler
{
    DispatchJobType Type { get; }

    Task HandleAsync(DispatchJob job, CancellationToken cancellationToken);
}
