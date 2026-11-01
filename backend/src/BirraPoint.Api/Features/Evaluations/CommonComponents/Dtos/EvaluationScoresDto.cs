namespace BirraPoint.Api.Features.Evaluations.CommonComponents.Dtos;

/// <summary>Wire shape for the five capped sections (contracts/rest-api.md §Judge workspace).</summary>
internal sealed record EvaluationScoresDto(int Aroma, int Appearance, int Flavor, int Mouthfeel, int Overall);
