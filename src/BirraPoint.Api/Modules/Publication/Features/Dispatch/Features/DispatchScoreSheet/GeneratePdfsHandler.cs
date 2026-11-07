using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Judging.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Publication.Domain;
using BuildingBlocks.Jobs;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.DispatchScoreSheet;

/// <summary>
/// FR-036/FR-040: renders one <see cref="GeneratedScoreSheet"/> per <see cref="ResultEntry"/> in
/// the competition (upsert by BeerEntryId so a retry never duplicates rows), then enqueues
/// <see cref="DispatchJobType.BundleZip"/> for the same competition. Payload is empty — the job's
/// own <see cref="Job.GroupKey"/> is all that's needed.
/// </summary>
internal sealed class GeneratePdfsHandler(
    AppDbContext dbContext,
    ICompetitionAccess competitionAccess,
    IResultRecipients resultRecipients,
    IJobQueue dispatchJobQueue,
    IStyleCatalog styleCatalog,
    IStoredEvaluations storedEvaluations) : IJobHandler
{
    public string Type => DispatchJobType.GeneratePdfs;

    public async Task HandleAsync(Job job, CancellationToken cancellationToken)
    {
        var competition = await competitionAccess.GetAsync(job.GroupKey, cancellationToken)
            ?? throw new InvalidOperationException($"Competition {job.GroupKey} not found.");

        var entries = await resultRecipients.GetEntriesAsync(job.GroupKey, cancellationToken);

        var styleByCode = await styleCatalog.GetByCodesAsync(
            entries.Select(e => e.StyleCode).Distinct(StringComparer.Ordinal).ToList(), cancellationToken);
        var evaluationsByEntry = await storedEvaluations.GetByEntriesAsync(
            entries.Select(e => e.EntryId).ToList(), cancellationToken);

        foreach (var entry in entries)
        {
            var style = styleByCode[entry.StyleCode];
            var evaluations = evaluationsByEntry[entry.EntryId];

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
                .FirstOrDefaultAsync(s => s.BeerEntryId == entry.EntryId, cancellationToken);

            if (existingSheet is null)
            {
                dbContext.GeneratedScoreSheets.Add(new GeneratedScoreSheet
                {
                    BeerEntryId = entry.EntryId,
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
            .OfDispatchType(DispatchJobType.BundleZip)
            .AnyAsync(j => j.GroupKey == job.GroupKey, cancellationToken);
        if (!bundleZipAlreadyQueued)
        {
            await dispatchJobQueue.EnqueueAsync(job.GroupKey, DispatchJobType.BundleZip, new { }, cancellationToken);
        }
    }
}
