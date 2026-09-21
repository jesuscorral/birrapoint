using System.Text.Json;
using FluentValidation;

namespace BirraPoint.Api.Features.Evaluations;

/// <summary>Shared (de)serialization for Evaluation.DescriptorsJson — camelCase, matching this
/// app's own HTTP JSON convention (Program.cs's ConfigureHttpJsonOptions), so the exact same DTO
/// shape round-trips through the wire and the jsonb column without a re-shaping step.</summary>
public static class EvaluationDescriptorsSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(EvaluationDescriptorsDto descriptors) =>
        JsonSerializer.Serialize(descriptors, Options);

    /// <summary>Never throws (senior-review M2): a row whose stored blob doesn't match the current
    /// DTO shape — a future field rename, a hand-edited row — must not 500 the organizer's audit
    /// drill-down or fail the whole competition's GeneratePdfsHandler job (runs inside the
    /// DispatchJob BackgroundService and would otherwise retry the same poisoned row forever).
    /// Falls back to "no descriptors recorded" instead, same as a genuinely null column.</summary>
    public static EvaluationDescriptorsDto? Deserialize(string? descriptorsJson)
    {
        if (descriptorsJson is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EvaluationDescriptorsDto>(descriptorsJson, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Session 2026-09-21: structured tasting-sheet descriptors, additive alongside the five
/// authoritative section scores/comments (EvaluationScoresDto/EvaluationCommentsDto) — never feed
/// SubmitEvaluationRules' caps or Evaluation.Total, which stay computed from the five scores alone
/// (FR-024, unchanged). Every field here is optional, both at the top level and within each nested
/// section object: a judge can fill as many or as few as they like, same as the paper BJCP sheet
/// this mirrors. Stored as-is (camelCase, this app's own HTTP JSON convention) in
/// Evaluation.DescriptorsJson — see ADR for why a jsonb string column instead of ~25 new columns.
/// </summary>
public sealed record EvaluationDescriptorsDto(
    AppearanceDescriptorsDto? Appearance,
    AromaDescriptorsDto? Aroma,
    FlavorDescriptorsDto? Flavor,
    MouthfeelDescriptorsDto? Mouthfeel,
    OverallDescriptorsDto? Overall,
    IReadOnlyList<string>? OffFlavors);

/// <summary>Apariencia. Color/Clarity/Foam are closed-list single choices (EvaluationDescriptorCatalog)
/// plus a free-text "Other" companion, matching the paper sheet's "Otros ____" line next to each.
/// Retention is a continuous 0–100 slider (Baja↔Alta, no discrete labels on the paper sheet).</summary>
public sealed record AppearanceDescriptorsDto(
    string? Color,
    string? ColorOther,
    bool ColorInappropriate,
    string? Clarity,
    string? Foam,
    string? FoamOther,
    bool FoamInappropriate,
    int? Retention,
    string? Texture,
    string? Notes);

/// <summary>Aroma. Malt/Hops/Fermentation are discrete 4-stop intensity sliders (Nada/Bajo/Medio/Alto
/// = 0–3), matching the paper sheet exactly.</summary>
public sealed record AromaDescriptorsDto(
    int? Malt,
    bool MaltInappropriate,
    int? Hops,
    bool HopsInappropriate,
    int? Fermentation);

/// <summary>Sabor. Malt/Hops/Bitterness/Fermentation are the same 0–3 discrete sliders as Aroma;
/// Balance and Finish are continuous 0–100 bipolar sliders (Lupulado↔Maltoso, Seco↔Dulce).</summary>
public sealed record FlavorDescriptorsDto(
    int? Malt,
    int? Hops,
    int? Bitterness,
    int? Fermentation,
    int? Balance,
    int? Finish);

/// <summary>Sensación en boca. All five attributes are 0–3 discrete sliders.</summary>
public sealed record MouthfeelDescriptorsDto(
    int? Body,
    bool BodyInappropriate,
    int? Carbonation,
    int? AlcoholWarmth,
    int? Creaminess,
    bool CreaminessInappropriate,
    int? Astringency,
    bool AstringencyInappropriate,
    string? Notes);

/// <summary>Impresión general. Three continuous 0–100 bipolar sliders (Ejemplo clásico↔No acorde al
/// estilo, Sin defectos↔Defectos significativos, Maravillosa↔Sin vida).</summary>
public sealed record OverallDescriptorsDto(
    int? ClassicExample,
    int? Defects,
    int? Vitality);

/// <summary>
/// Closed lists for every enum-like descriptor field, validated by
/// SubmitEvaluationCommandValidator/CorrectEvaluationCommandValidator (same "closed list, reject
/// unknown values" convention as the BJCP style catalog and the ProblemDetails error URN catalog).
/// English tokens, same convention as EvaluationStatus/CompetitionState — the frontend maps these to
/// their Spanish display labels; the wire/storage values themselves stay English.
/// </summary>
public static class EvaluationDescriptorCatalog
{
    public static readonly IReadOnlyList<string> ColorOptions =
        ["Yellow", "Golden", "Amber", "Copper", "Brown", "Black", "Other"];

    public static readonly IReadOnlyList<string> ClarityOptions = ["Clear", "Hazy", "Opaque"];

    public static readonly IReadOnlyList<string> FoamOptions =
        ["White", "Ivory", "Beige", "Tan", "Brown", "Other"];

    /// <summary>The 20 fixed off-flavor descriptor terms from the paper sheet's sidebar checklist.</summary>
    public static readonly IReadOnlyList<string> OffFlavorTerms =
    [
        "Acetaldehyde", "SourAcidic", "Smoky", "Alcoholic", "Astringent", "Brettanomyces",
        "Diacetyl", "Dms", "Spicy", "Estery", "Lightstruck", "Herbal", "Medicinal", "Metallic",
        "Oxidized", "Plastic", "MustyMoldy", "SolventFusel", "Sulfury", "Vegetal",
    ];
}

/// <summary>
/// Every field is optional (see EvaluationDescriptorsDto's own doc comment) — this validator only
/// rejects a field that WAS supplied but is out of range/not in its closed list, it never requires
/// presence. Shared by SubmitEvaluationCommandValidator and CorrectEvaluationCommandValidator via
/// `.SetValidator(...)` (FluentValidation's nested-validator mechanism) so the two boundaries can't
/// drift apart — unlike EvaluationScoresDto/EvaluationCommentsDto's rules, which are intentionally
/// duplicated per this codebase's existing convention, this one is new enough (Session 2026-09-21)
/// to start shared instead.
/// </summary>
public sealed class EvaluationDescriptorsDtoValidator : AbstractValidator<EvaluationDescriptorsDto>
{
    public EvaluationDescriptorsDtoValidator()
    {
        RuleFor(d => d.Appearance!).SetValidator(new AppearanceDescriptorsDtoValidator()).When(d => d.Appearance is not null);
        RuleFor(d => d.Aroma!).SetValidator(new AromaDescriptorsDtoValidator()).When(d => d.Aroma is not null);
        RuleFor(d => d.Flavor!).SetValidator(new FlavorDescriptorsDtoValidator()).When(d => d.Flavor is not null);
        RuleFor(d => d.Mouthfeel!).SetValidator(new MouthfeelDescriptorsDtoValidator()).When(d => d.Mouthfeel is not null);
        RuleFor(d => d.Overall!).SetValidator(new OverallDescriptorsDtoValidator()).When(d => d.Overall is not null);

        RuleForEach(d => d.OffFlavors)
            .Must(term => EvaluationDescriptorCatalog.OffFlavorTerms.Contains(term))
            .WithMessage("'{PropertyValue}' is not a recognized off-flavor descriptor.")
            .When(d => d.OffFlavors is not null);

        // senior-review B2: the closed-list check above rejects an unrecognized TERM, but says
        // nothing about how many entries the list itself may hold — without this, an authenticated
        // judge could submit the same (or distinct) valid term thousands of times.
        RuleFor(d => d.OffFlavors!)
            .Must(list => list.Count <= EvaluationDescriptorCatalog.OffFlavorTerms.Count)
            .WithMessage("Too many off-flavor descriptors.")
            .When(d => d.OffFlavors is not null);
    }
}

file static class DescriptorValidatorRules
{
    /// <summary>4-stop discrete intensity slider (Nada/Bajo/Medio/Alto).</summary>
    public static IRuleBuilderOptions<T, int?> DiscreteIntensity<T>(this IRuleBuilder<T, int?> rule) =>
        rule.InclusiveBetween(0, 3).When(x => x is not null);

    /// <summary>Continuous bipolar slider (e.g. Lupulado↔Maltoso, Seco↔Dulce).</summary>
    public static IRuleBuilderOptions<T, int?> BipolarSlider<T>(this IRuleBuilder<T, int?> rule) =>
        rule.InclusiveBetween(0, 100).When(x => x is not null);
}

file sealed class AppearanceDescriptorsDtoValidator : AbstractValidator<AppearanceDescriptorsDto>
{
    // senior-review B2: every other free-text column in this codebase has an explicit max length
    // (2000 for the five section comments, 4000 for FeedbackComment) — these "Otros"/notes fields
    // are advisory annotations, not another comment box, so a shorter cap is appropriate; jsonb
    // itself has no length limit, so without this an authenticated judge could otherwise persist
    // an arbitrarily large string per field.
    private const int FreeTextMaxLength = 500;

    public AppearanceDescriptorsDtoValidator()
    {
        RuleFor(d => d.Color).Must(v => EvaluationDescriptorCatalog.ColorOptions.Contains(v!)).When(d => d.Color is not null);
        RuleFor(d => d.ColorOther).MaximumLength(FreeTextMaxLength);
        RuleFor(d => d.Clarity).Must(v => EvaluationDescriptorCatalog.ClarityOptions.Contains(v!)).When(d => d.Clarity is not null);
        RuleFor(d => d.Foam).Must(v => EvaluationDescriptorCatalog.FoamOptions.Contains(v!)).When(d => d.Foam is not null);
        RuleFor(d => d.FoamOther).MaximumLength(FreeTextMaxLength);
        RuleFor(d => d.Retention).BipolarSlider();
        RuleFor(d => d.Texture).MaximumLength(FreeTextMaxLength);
        RuleFor(d => d.Notes).MaximumLength(FreeTextMaxLength);
    }
}

file sealed class AromaDescriptorsDtoValidator : AbstractValidator<AromaDescriptorsDto>
{
    public AromaDescriptorsDtoValidator()
    {
        RuleFor(d => d.Malt).DiscreteIntensity();
        RuleFor(d => d.Hops).DiscreteIntensity();
        RuleFor(d => d.Fermentation).DiscreteIntensity();
    }
}

file sealed class FlavorDescriptorsDtoValidator : AbstractValidator<FlavorDescriptorsDto>
{
    public FlavorDescriptorsDtoValidator()
    {
        RuleFor(d => d.Malt).DiscreteIntensity();
        RuleFor(d => d.Hops).DiscreteIntensity();
        RuleFor(d => d.Bitterness).DiscreteIntensity();
        RuleFor(d => d.Fermentation).DiscreteIntensity();
        RuleFor(d => d.Balance).BipolarSlider();
        RuleFor(d => d.Finish).BipolarSlider();
    }
}

file sealed class MouthfeelDescriptorsDtoValidator : AbstractValidator<MouthfeelDescriptorsDto>
{
    public MouthfeelDescriptorsDtoValidator()
    {
        RuleFor(d => d.Body).DiscreteIntensity();
        RuleFor(d => d.Carbonation).DiscreteIntensity();
        RuleFor(d => d.AlcoholWarmth).DiscreteIntensity();
        RuleFor(d => d.Creaminess).DiscreteIntensity();
        RuleFor(d => d.Astringency).DiscreteIntensity();
        RuleFor(d => d.Notes).MaximumLength(500);
    }
}

file sealed class OverallDescriptorsDtoValidator : AbstractValidator<OverallDescriptorsDto>
{
    public OverallDescriptorsDtoValidator()
    {
        RuleFor(d => d.ClassicExample).BipolarSlider();
        RuleFor(d => d.Defects).BipolarSlider();
        RuleFor(d => d.Vitality).BipolarSlider();
    }
}
