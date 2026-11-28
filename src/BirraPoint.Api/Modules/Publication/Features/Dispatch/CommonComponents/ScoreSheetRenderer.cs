using BirraPoint.Contracts.Catalog;
using BirraPoint.Contracts.Judging.Dtos;
using BirraPoint.Contracts.Organization;
using QuestPDF.Fluent;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.CommonComponents;

/// <summary>Renders one entry's score sheet PDF (FR-040); shared by the first generation and the refresh after a
/// correction (FR-083) so both produce the same document.</summary>
internal static class ScoreSheetRenderer
{
    public static byte[] Render(
        string competitionName, ResultEntry entry, StyleSummaryDto style, IReadOnlyList<StoredEvaluationDto> evaluations)
    {
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
        // zero rather than "not evaluated". Mirrors CloseTableRules.ComputeMean's rounding convention
        // without importing across feature folders.
        decimal? consolidatedMean = evaluations.Count == 0
            ? null
            : Math.Round(evaluations.Average(x => (decimal)x.Total), 2, MidpointRounding.AwayFromZero);

        return new ScoreSheetDocument(
            competitionName, entry.BlindCode, entry.StyleCode, style.Name, judgeEntries, consolidatedMean)
            .GeneratePdf();
    }
}
