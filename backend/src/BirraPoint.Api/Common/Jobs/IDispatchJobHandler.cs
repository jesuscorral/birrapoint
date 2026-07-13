using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Common.Jobs;

/// <summary>
/// Extensibility point for a <see cref="DispatchJobType"/>: implementations are auto-discovered
/// by <see cref="DispatchWorker"/> via <c>IEnumerable&lt;IDispatchJobHandler&gt;</c>. Must be
/// idempotent (R-06) — <see cref="DispatchWorker"/> may invoke the same job more than once after
/// a crash mid-processing. T016 registers none; the first handlers are T041 (SendInvitation) and
/// T075 (GeneratePdfs/BundleZip/SendResultEmail).
/// </summary>
public interface IDispatchJobHandler
{
    DispatchJobType Type { get; }

    Task HandleAsync(DispatchJob job, CancellationToken cancellationToken);
}
