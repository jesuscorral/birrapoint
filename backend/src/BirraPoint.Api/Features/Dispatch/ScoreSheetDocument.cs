using BirraPoint.Api.Features.Evaluations;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BirraPoint.Api.Features.Dispatch;

/// <summary>One judge's submitted scores/comments for a sample, rendered by <see cref="ScoreSheetDocument"/>.
/// Descriptors/Feedback (Session 2026-09-21) are the structured tasting-sheet data alongside the
/// five authoritative scores/comments — advisory, never part of <see cref="Total"/>.</summary>
public sealed record ScoreSheetJudgeEntry(
    string JudgeDisplayName,
    int AromaScore, string AromaComment,
    int AppearanceScore, string AppearanceComment,
    int FlavorScore, string FlavorComment,
    int MouthfeelScore, string MouthfeelComment,
    int OverallScore, string OverallComment,
    int Total,
    EvaluationDescriptorsDto? Descriptors,
    string? Feedback);

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

                    var descriptorLines = FormatDescriptorLines(entry.Descriptors);
                    foreach (var line in descriptorLines)
                    {
                        column.Item().Text(line).FontSize(9).FontColor(Colors.Grey.Darken1);
                    }

                    if (!string.IsNullOrWhiteSpace(entry.Feedback))
                    {
                        column.Item().PaddingTop(4).Text($"Feedback: {entry.Feedback}").Italic();
                    }
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

    /// <summary>Compact "Field: value" lines for whichever descriptor fields this judge actually
    /// filled — omits every null field rather than printing a wall of empty attributes, and omits
    /// a section entirely when nothing in it was filled. Session 2026-09-21, advisory data only
    /// (see ScoreSheetJudgeEntry's own doc comment); a first pass in plain English tokens, same as
    /// the rest of this document — not localized (tracked as follow-up debt, matches
    /// Docs/arquitectura_viva.md's existing PDF-not-yet-i18n'd note).</summary>
    private static IReadOnlyList<string> FormatDescriptorLines(EvaluationDescriptorsDto? descriptors)
    {
        if (descriptors is null)
        {
            return [];
        }

        var lines = new List<string>();

        void AddSection(string label, params (string Field, object? Value)[] fields)
        {
            var parts = fields
                .Where(f => f.Value is not null && f.Value is not false)
                .Select(f => f.Value is true ? f.Field : $"{f.Field}={f.Value}")
                .ToList();
            if (parts.Count > 0)
            {
                lines.Add($"{label}: {string.Join(", ", parts)}");
            }
        }

        if (descriptors.Appearance is { } appearance)
        {
            AddSection(
                "Appearance descriptors",
                ("Color", appearance.Color == "Other" ? appearance.ColorOther : appearance.Color),
                ("ColorInappropriate", appearance.ColorInappropriate),
                ("Clarity", appearance.Clarity),
                ("Foam", appearance.Foam == "Other" ? appearance.FoamOther : appearance.Foam),
                ("FoamInappropriate", appearance.FoamInappropriate),
                ("Retention", appearance.Retention),
                ("Texture", appearance.Texture),
                ("Notes", appearance.Notes));
        }

        if (descriptors.Aroma is { } aroma)
        {
            AddSection(
                "Aroma descriptors",
                ("Malt", aroma.Malt), ("MaltInappropriate", aroma.MaltInappropriate),
                ("Hops", aroma.Hops), ("HopsInappropriate", aroma.HopsInappropriate),
                ("Fermentation", aroma.Fermentation));
        }

        if (descriptors.Flavor is { } flavor)
        {
            AddSection(
                "Flavor descriptors",
                ("Malt", flavor.Malt), ("Hops", flavor.Hops), ("Bitterness", flavor.Bitterness),
                ("Fermentation", flavor.Fermentation), ("Balance", flavor.Balance), ("Finish", flavor.Finish));
        }

        if (descriptors.Mouthfeel is { } mouthfeel)
        {
            AddSection(
                "Mouthfeel descriptors",
                ("Body", mouthfeel.Body), ("BodyInappropriate", mouthfeel.BodyInappropriate),
                ("Carbonation", mouthfeel.Carbonation), ("AlcoholWarmth", mouthfeel.AlcoholWarmth),
                ("Creaminess", mouthfeel.Creaminess), ("CreaminessInappropriate", mouthfeel.CreaminessInappropriate),
                ("Astringency", mouthfeel.Astringency), ("AstringencyInappropriate", mouthfeel.AstringencyInappropriate),
                ("Notes", mouthfeel.Notes));
        }

        if (descriptors.Overall is { } overall)
        {
            AddSection(
                "Overall descriptors",
                ("ClassicExample", overall.ClassicExample), ("Defects", overall.Defects), ("Vitality", overall.Vitality));
        }

        if (descriptors.OffFlavors is { Count: > 0 } offFlavors)
        {
            lines.Add($"Off-flavors noted: {string.Join(", ", offFlavors)}");
        }

        return lines;
    }
}
