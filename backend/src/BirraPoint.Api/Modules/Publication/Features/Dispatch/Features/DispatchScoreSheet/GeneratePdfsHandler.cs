using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Judging.Contracts;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Publication.Domain;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.DispatchScoreSheet;

/// <summary>
/// FR-036/FR-040: renders one <see cref="GeneratedScoreSheet"/> per <see cref="BeerEntry"/> in
/// the competition (upsert by BeerEntryId so a retry never duplicates rows), then enqueues
/// <see cref="DispatchJobType.BundleZip"/> for the same competition. Payload is empty — the job's
/// own <see cref="DispatchJob.CompetitionId"/> is all that's needed.
/// </summary>
internal sealed class GeneratePdfsHandler(
    AppDbContext dbContext, IDispatchJobQueue dispatchJobQueue, IStyleCatalog styleCatalog, IStoredEvaluations storedEvaluations) : IDispatchJobHandler
{
    public DispatchJobType Type => DispatchJobType.GeneratePdfs;

    public async Task HandleAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions.FirstAsync(c => c.Id == job.CompetitionId, cancellationToken);

        var entries = await dbContext.BeerEntries
            .Where(e => e.CompetitionId == job.CompetitionId)
            .ToListAsync(cancellationToken);

        var styleByCode = await styleCatalog.GetByCodesAsync(
            entries.Select(e => e.StyleCode).Distinct(StringComparer.Ordinal).ToList(), cancellationToken);
        var evaluationsByEntry = await storedEvaluations.GetByEntriesAsync(
            entries.Select(e => e.Id).ToList(), cancellationToken);

        foreach (var entry in entries)
        {
            var style = styleByCode[entry.StyleCode];
            var evaluations = evaluationsByEntry[entry.Id];

            var judgeEntries = evaluations
                .Select(x => new ScoreSheetJudgeEntry(
                    x.JudgeDisplayName,
                    x.AromaScore, x.AromaComment,
                    x.AppearanceScore, x.AppearanceComment,
                    x.FlavorScore, x.FlavorComment,
                    x.MouthfeelScore, x.MouthfeelComment,
                    x.OverallScore, x.OverallComment,
                    x.Total,
                    x.Descriptors,
                    x.Feedback))
                .ToList();

            // Null (not 0) when nobody has evaluated this entry — 0 would read as a real score of
            // zero rather than "not evaluated". Mirrors
            // CloseTableRules.ComputeMean's rounding convention without importing across feature
            // folders (same deliberate duplication already used by GetEntryEvaluationsQueryHandler).
            decimal? consolidatedMean = evaluations.Count == 0
                ? null
                : Math.Round(evaluations.Average(x => (decimal)x.Total), 2, MidpointRounding.AwayFromZero);

            var document = new ScoreSheetDocument(
                competition.Name, entry.BlindCode, entry.StyleCode, style.Name, judgeEntries, consolidatedMean);
            var pdfBytes = document.GeneratePdf();

            var existingSheet = await dbContext.GeneratedScoreSheets
                .FirstOrDefaultAsync(s => s.BeerEntryId == entry.Id, cancellationToken);

            if (existingSheet is null)
            {
                dbContext.GeneratedScoreSheets.Add(new GeneratedScoreSheet
                {
                    BeerEntryId = entry.Id,
                    PdfBytes = pdfBytes,
                    GeneratedAt = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                existingSheet.PdfBytes = pdfBytes;
                existingSheet.GeneratedAt = DateTimeOffset.UtcNow;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // Same at-least-once guard as BundleZipHandler's fan-out:
        // DispatchWorker may re-run this handler after a crash even though a prior
        // attempt's BundleZip enqueue already committed durably — without this check, a re-run
        // would enqueue a second BundleZip job (and, transitively, a second round of emails).
        var bundleZipAlreadyQueued = await dbContext.DispatchJobs
            .AnyAsync(j => j.CompetitionId == job.CompetitionId && j.Type == DispatchJobType.BundleZip, cancellationToken);
        if (!bundleZipAlreadyQueued)
        {
            await dispatchJobQueue.EnqueueAsync(job.CompetitionId, DispatchJobType.BundleZip, new { }, cancellationToken);
        }
    }
}
