namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;

/// <summary>Sabor. Malt/Hops/Bitterness/Fermentation are the same 0–3 discrete sliders as Aroma;
/// Balance and Finish are continuous 0–100 bipolar sliders (Lupulado↔Maltoso, Seco↔Dulce). Every
/// attribute carries its own Inappropriate flag.</summary>
internal sealed record FlavorDescriptorsDto(
    int? Malt,
    bool MaltInappropriate,
    int? Hops,
    bool HopsInappropriate,
    int? Bitterness,
    bool BitternessInappropriate,
    int? Fermentation,
    bool FermentationInappropriate,
    int? Balance,
    bool BalanceInappropriate,
    int? Finish,
    bool FinishInappropriate);
