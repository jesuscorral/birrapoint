namespace BirraPoint.Api.Modules.Judging.Contracts.Dtos;

/// <summary>Aroma. Malt/Hops/Fermentation are discrete 4-stop intensity sliders (Nada/Bajo/Medio/Alto
/// = 0–3), matching the paper sheet exactly. Every attribute carries its own Inappropriate flag.</summary>
internal sealed record AromaDescriptorsDto(
    int? Malt,
    bool MaltInappropriate,
    int? Hops,
    bool HopsInappropriate,
    int? Fermentation,
    bool FermentationInappropriate);
