
namespace BirraPoint.Api.Features.Evaluations.CommonComponents;

/// <summary>
/// Closed lists for every enum-like descriptor field, validated by
/// SubmitEvaluationCommandValidator/CorrectEvaluationCommandValidator (same "closed list, reject
/// unknown values" convention as the BJCP style catalog and the ProblemDetails error URN catalog).
/// English tokens, same convention as EvaluationStatus/CompetitionState — the frontend maps these to
/// their Spanish display labels; the wire/storage values themselves stay English.
/// </summary>
internal static class EvaluationDescriptorCatalog
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
