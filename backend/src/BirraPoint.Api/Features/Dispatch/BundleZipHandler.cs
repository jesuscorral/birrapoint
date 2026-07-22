using System.IO.Compression;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Dispatch;

/// <summary>
/// T074/FR-040: bundles every <see cref="GeneratedScoreSheet"/> for the competition into a single
/// in-memory ZIP at <see cref="DispatchPaths.ZipEntryPath"/>, upserts the <see
/// cref="ResultsArchive"/> row (by CompetitionId, so a retry never duplicates it), then enqueues one
/// <see cref="DispatchJobType.SendResultEmail"/> job per participant (FR-041) — each recipient's
/// delivery status/attempts/lastError live entirely on that job's own row, the same way
/// <see cref="Invitation"/> tracks judge invites, except here there's no separate status entity.
/// </summary>
public sealed class BundleZipHandler(AppDbContext dbContext, IDispatchJobQueue dispatchJobQueue) : IDispatchJobHandler
{
    public DispatchJobType Type => DispatchJobType.BundleZip;

    public async Task HandleAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions.FirstAsync(c => c.Id == job.CompetitionId, cancellationToken);

        var sheets = await dbContext.BeerEntries
            .Where(e => e.CompetitionId == job.CompetitionId)
            .Join(dbContext.GeneratedScoreSheets, e => e.Id, s => s.BeerEntryId,
                (e, s) => new { e.ParticipantId, e.StyleCode, e.BlindCode, s.PdfBytes })
            .ToListAsync(cancellationToken);

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var sheet in sheets)
            {
                var entryPath = DispatchPaths.ZipEntryPath(competition.Name, sheet.ParticipantId, sheet.StyleCode, sheet.BlindCode);
                var zipEntry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                await using var entryStream = zipEntry.Open();
                await entryStream.WriteAsync(sheet.PdfBytes, cancellationToken);
            }
        }

        var zipBytes = memoryStream.ToArray();

        var existingArchive = await dbContext.ResultsArchives
            .FirstOrDefaultAsync(a => a.CompetitionId == job.CompetitionId, cancellationToken);

        if (existingArchive is null)
        {
            dbContext.ResultsArchives.Add(new ResultsArchive
            {
                CompetitionId = job.CompetitionId,
                ZipBytes = zipBytes,
                GeneratedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existingArchive.ZipBytes = zipBytes;
            existingArchive.GeneratedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var participantIds = await dbContext.Participants
            .Where(p => p.CompetitionId == job.CompetitionId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var participantId in participantIds)
        {
            await dispatchJobQueue.EnqueueAsync(
                job.CompetitionId, DispatchJobType.SendResultEmail, new SendResultEmailPayload(participantId), cancellationToken);
        }
    }
}
