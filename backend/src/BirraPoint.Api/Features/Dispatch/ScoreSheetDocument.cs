using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BirraPoint.Api.Features.Dispatch;

/// <summary>One judge's submitted scores/comments for a sample, rendered by <see cref="ScoreSheetDocument"/>.</summary>
public sealed record ScoreSheetJudgeEntry(
    string JudgeDisplayName,
    int AromaScore, string AromaComment,
    int AppearanceScore, string AppearanceComment,
    int FlavorScore, string FlavorComment,
    int MouthfeelScore, string MouthfeelComment,
    int OverallScore, string OverallComment,
    int Total);

/// <summary>
/// T074/FR-040: one-page-per-entry QuestPDF scoresheet — competition name, blind code, style,
/// every judge's scores/comments/total, and the consolidated mean. Deliberately omits participant
/// and beer name anywhere in the content (R-14/BR-01) — those only appear in the ZIP folder path
/// built by <see cref="DispatchPaths"/>, never inside the PDF itself.
/// </summary>
public sealed class ScoreSheetDocument(
    string competitionName, string blindCode, string styleCode, string styleName,
    IReadOnlyList<ScoreSheetJudgeEntry> judgeEntries, decimal? consolidatedMean)
    : IDocument
{
    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(30);

            page.Header().Text($"{competitionName} — {blindCode} ({styleCode} {styleName})").FontSize(16).Bold();

            page.Content().Column(column =>
            {
                foreach (var entry in judgeEntries)
                {
                    column.Item().PaddingTop(10).Text($"Judge: {entry.JudgeDisplayName}").Bold();
                    column.Item().Text($"Aroma: {entry.AromaScore} — {entry.AromaComment}");
                    column.Item().Text($"Appearance: {entry.AppearanceScore} — {entry.AppearanceComment}");
                    column.Item().Text($"Flavor: {entry.FlavorScore} — {entry.FlavorComment}");
                    column.Item().Text($"Mouthfeel: {entry.MouthfeelScore} — {entry.MouthfeelComment}");
                    column.Item().Text($"Overall: {entry.OverallScore} — {entry.OverallComment}");
                    column.Item().Text($"Total: {entry.Total}").Bold();
                }

                // Null (not 0) when nobody has evaluated this entry yet — 0 would read as a real
                // score of zero rather than "not evaluated" (senior-code-reviewer finding on PR
                // #25; matches GetEntryEvaluationsQueryHandler's null-until-closed convention).
                column.Item().PaddingTop(20)
                    .Text($"Consolidated mean: {(consolidatedMean.HasValue ? consolidatedMean.Value.ToString() : "not evaluated")}")
                    .FontSize(14).Bold();
            });
        });
    }
}
