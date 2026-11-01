namespace BirraPoint.Api.Features.Evaluations.CommonComponents.Dtos;

/// <summary>Wire shape for the five per-section comments (FR-025: each ≥20 chars).</summary>
internal sealed record EvaluationCommentsDto(string Aroma, string Appearance, string Flavor, string Mouthfeel, string Overall);
