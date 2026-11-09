using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Judging.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Publication.Domain;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.CommonComponents;
using BuildingBlocks.Jobs;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.RefreshResults;

/// <summary>
/// FR-083: after a correction on a finalized competition, regenerates that entry's score sheet if one exists and
/// rebuilds the results ZIP if one exists, so downloads show the corrected total. Participants are not emailed
/// again. When the pipeline has not produced a sheet or an archive yet there is nothing stale to refresh, and the
/// job completes (the pipeline will read the corrected data itself).
/// </summary>
internal sealed class RefreshResultsHandler(
    AppDbContext dbContext,
    ICompetitionAccess competitionAccess,
    IResultRecipients resultRecipients,
    IStyleCatalog styleCatalog,
    IStoredEvaluations storedEvaluations,
    ResultsArchiveBuilder archiveBuilder) : IJobHandler
{
    public string Type => DispatchJobType.RefreshResults;

    public async Task HandleAsync(Job job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<RefreshResultsPayload>(job.PayloadJson)
            ?? throw new InvalidOperationException("RefreshResults payload is empty.");

        var sheet = await dbContext.GeneratedScoreSheets
            .FirstOrDefaultAsync(s => s.BeerEntryId == payload.BeerEntryId, cancellationToken);
        if (sheet is not null)
        {
            await RegenerateSheetAsync(job.GroupKey, payload.BeerEntryId, sheet, cancellationToken);
        }

        if (await dbContext.ResultsArchives.AnyAsync(a => a.CompetitionId == job.GroupKey, cancellationToken))
        {
            await archiveBuilder.BuildAsync(job.GroupKey, cancellationToken);
        }
    }

    private async Task RegenerateSheetAsync(
        Guid competitionId, Guid beerEntryId, GeneratedScoreSheet sheet, CancellationToken cancellationToken)
    {
        var competition = await competitionAccess.GetAsync(competitionId, cancellationToken)
            ?? throw new InvalidOperationException($"Competition {competitionId} not found.");
        var entry = (await resultRecipients.GetEntriesAsync(competitionId, cancellationToken))
            .FirstOrDefault(e => e.EntryId == beerEntryId);
        if (entry is null)
        {
            return;
        }

        var style = (await styleCatalog.GetByCodesAsync([entry.StyleCode], cancellationToken))[entry.StyleCode];
        var evaluations = (await storedEvaluations.GetByEntriesAsync([beerEntryId], cancellationToken))[beerEntryId];

        sheet.PdfBytes = ScoreSheetRenderer.Render(competition.Name, entry, style, evaluations);
        sheet.GeneratedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
