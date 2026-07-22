using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Dispatch;

/// <summary>Exactly one of <see cref="ZipBytes"/>/<see cref="Status"/> is set: the archive is ready
/// (200, stream the bytes) or it's still being generated (202, report the status).</summary>
public sealed record ResultsArchiveResult(byte[]? ZipBytes, string? Status);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record GetResultsArchiveQuery(Guid CompetitionId) : IRequest<ResultsArchiveResult?>;

public sealed class GetResultsArchiveQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetResultsArchiveQuery, ResultsArchiveResult?>
{
    public async Task<ResultsArchiveResult?> Handle(GetResultsArchiveQuery request, CancellationToken cancellationToken)
    {
        var owns = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);
        if (!owns)
        {
            return null;
        }

        var archive = await dbContext.ResultsArchives
            .FirstOrDefaultAsync(a => a.CompetitionId == request.CompetitionId, cancellationToken);
        if (archive is not null)
        {
            return new ResultsArchiveResult(archive.ZipBytes, null);
        }

        // No BundleZip job yet implies the pipeline hasn't reached that stage — report Pending
        // rather than leaking "no job found" as a distinct state the client would need to handle.
        var bundleJob = await dbContext.DispatchJobs
            .Where(j => j.CompetitionId == request.CompetitionId && j.Type == DispatchJobType.BundleZip)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new ResultsArchiveResult(null, (bundleJob?.Status ?? DispatchJobStatus.Pending).ToString());
    }
}
