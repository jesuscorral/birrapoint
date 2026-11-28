namespace BirraPoint.Contracts.Judging.Dtos;

/// <summary>Impresión general. Three continuous 0–100 bipolar sliders (Ejemplo clásico↔No acorde al
/// estilo, Sin defectos↔Defectos significativos, Maravillosa↔Sin vida), each with its own
/// Inappropriate flag.</summary>
public sealed record OverallDescriptorsDto(
    int? ClassicExample,
    bool ClassicExampleInappropriate,
    int? Defects,
    bool DefectsInappropriate,
    int? Vitality,
    bool VitalityInappropriate);
