using System.IO.Compression;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Publication.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.CommonComponents;

/// <summary>
/// FR-040: bundles every <see cref="GeneratedScoreSheet"/> of a competition into one in-memory ZIP at
/// <see cref="DispatchPaths.ZipEntryPath"/> and upserts the <see cref="ResultsArchive"/> row (by CompetitionId, so a
/// retry never duplicates it). Shared by the dispatch pipeline and the refresh after a correction (FR-083).
/// </summary>
internal sealed class ResultsArchiveBuilder(
    AppDbContext dbContext, ICompetitionAccess competitionAccess, IResultRecipients resultRecipients)
{
    /// <summary>Rebuilds the archive from the sheets stored now and saves it.</summary>
    public async Task BuildAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        var competition = await competitionAccess.GetAsync(competitionId, cancellationToken)
            ?? throw new InvalidOperationException($"Competition {competitionId} not found.");

        var entries = await resultRecipients.GetEntriesAsync(competitionId, cancellationToken);
        var entryIds = entries.Select(e => e.EntryId).ToList();
        var pdfByEntryId = await dbContext.GeneratedScoreSheets
            .Where(s => entryIds.Contains(s.BeerEntryId))
            .Select(s => new { s.BeerEntryId, s.PdfBytes })
            .ToDictionaryAsync(s => s.BeerEntryId, s => s.PdfBytes, cancellationToken);
        var sheets = entries
            .Where(e => pdfByEntryId.ContainsKey(e.EntryId))
            .Select(e => new { e.ParticipantId, e.StyleCode, e.BlindCode, PdfBytes = pdfByEntryId[e.EntryId] })
            .ToList();

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var sheet in sheets)
            {
                var entryPath = DispatchPaths.ZipEntryPath(competition.Name, sheet.ParticipantId, sheet.StyleCode, sheet.BlindCode);
                var zipEntry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                await using var entryStream = await zipEntry.OpenAsync(cancellationToken);
                await entryStream.WriteAsync(sheet.PdfBytes, cancellationToken);
            }
        }

        var zipBytes = memoryStream.ToArray();

        var existingArchive = await dbContext.ResultsArchives
            .FirstOrDefaultAsync(a => a.CompetitionId == competitionId, cancellationToken);

        if (existingArchive is null)
        {
            dbContext.ResultsArchives.Add(new ResultsArchive
            {
                CompetitionId = competitionId,
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
    }
}
