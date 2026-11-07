namespace BuildingBlocks.Jobs;

/// <summary>
/// Extension point for one job <see cref="Type"/>: implementations are discovered by the worker through
/// <c>IEnumerable&lt;IJobHandler&gt;</c> (register them scoped). Must be idempotent — the worker may invoke the
/// same job more than once after a crash mid-processing.
/// </summary>
public interface IJobHandler
{
    string Type { get; }

    Task HandleAsync(Job job, CancellationToken cancellationToken);
}
