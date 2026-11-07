using BirraPoint.Api.Domain;
using BuildingBlocks.Jobs;

namespace BirraPoint.Api.Common.Persistence;

internal static class DispatchJobQueries
{
    /// <summary>Filters jobs to one <see cref="DispatchJobType"/>. A plain <c>==</c> is the only string comparison
    /// EF Core translates (the <c>StringComparison</c> overloads of <c>string.Equals</c> are rejected), so the single
    /// equality lives here instead of tripping MA0006 at every call site.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Meziantou.Analyzer", "MA0006:Use string.Equals instead of Equals operator",
        Justification = "Expression tree translated by EF Core, which cannot translate string.Equals with a StringComparison.")]
    public static IQueryable<Job> OfDispatchType(this IQueryable<Job> jobs, string type) =>
        jobs.Where(j => j.Type == type);
}
