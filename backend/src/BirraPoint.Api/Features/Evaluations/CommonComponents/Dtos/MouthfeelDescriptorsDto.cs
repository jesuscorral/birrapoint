namespace BirraPoint.Api.Features.Evaluations.CommonComponents.Dtos;

/// <summary>Sensación en boca. All five attributes are 0–3 discrete sliders, each with its own
/// Inappropriate flag.</summary>
internal sealed record MouthfeelDescriptorsDto(
    int? Body,
    bool BodyInappropriate,
    int? Carbonation,
    bool CarbonationInappropriate,
    int? AlcoholWarmth,
    bool AlcoholWarmthInappropriate,
    int? Creaminess,
    bool CreaminessInappropriate,
    int? Astringency,
    bool AstringencyInappropriate,
    string? Notes);
