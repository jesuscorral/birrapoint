using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace BirraPoint.Api.Features.Dispatch;

/// <summary>
/// T074/FR-036/FR-040: renders one <see cref="GeneratedScoreSheet"/> per <see cref="BeerEntry"/> in
/// the competition (upsert by BeerEntryId so a retry never duplicates rows), then enqueues
/// <see cref="DispatchJobType.BundleZip"/> for the same competition. Payload is empty — the job's
/// own <see cref="DispatchJob.CompetitionId"/> is all that's needed.
/// </summary>
public sealed class GeneratePdfsHandler(AppDbContext dbContext, IDispatchJobQueue dispatchJobQueue) : IDispatchJobHandler
{
    public DispatchJobType Type => DispatchJobType.GeneratePdfs;

    public async Task HandleAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions.FirstAsync(c => c.Id == job.CompetitionId, cancellationToken);

        var entries = await dbContext.BeerEntries
            .Where(e => e.CompetitionId == job.CompetitionId)
            .ToListAsync(cancellationToken);

        foreach (var entry in entries)
        {
            var style = await dbContext.BjcpStyles.FirstAsync(s => s.Code == entry.StyleCode, cancellationToken);

            // Same join shape as GetEntryEvaluationsQueryHandler (Features/Monitoring).
            var evaluations = await dbContext.Evaluations
                .AsNoTracking()
                .Where(ev => ev.BeerEntryId == entry.Id)
                .Join(dbContext.Judges, ev => ev.JudgeId, j => j.Id, (ev, j) => new { Evaluation = ev, j.DisplayName })
                .ToListAsync(cancellationToken);

            var judgeEntries = evaluations
                .Select(x => new ScoreSheetJudgeEntry(
                    x.DisplayName,
                    x.Evaluation.AromaScore, x.Evaluation.AromaComment,
                    x.Evaluation.AppearanceScore, x.Evaluation.AppearanceComment,
                    x.Evaluation.FlavorScore, x.Evaluation.FlavorComment,
                    x.Evaluation.MouthfeelScore, x.Evaluation.MouthfeelComment,
                    x.Evaluation.OverallScore, x.Evaluation.OverallComment,
                    x.Evaluation.Total))
                .ToList();

            // Mirrors CloseTableRules.ComputeMean's rounding convention without importing across
            // feature folders (same deliberate duplication already used by GetEntryEvaluationsQueryHandler).
            var consolidatedMean = evaluations.Count == 0
                ? 0m
                : Math.Round(evaluations.Average(x => (decimal)x.Evaluation.Total), 2, MidpointRounding.AwayFromZero);

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

        await dispatchJobQueue.EnqueueAsync(job.CompetitionId, DispatchJobType.BundleZip, new { }, cancellationToken);
    }
}
