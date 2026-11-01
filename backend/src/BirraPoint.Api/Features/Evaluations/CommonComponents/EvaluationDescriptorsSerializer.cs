using System.Text.Json;
using BirraPoint.Api.Features.Evaluations.CommonComponents.Dtos;

namespace BirraPoint.Api.Features.Evaluations.CommonComponents;

/// <summary>Shared (de)serialization for Evaluation.DescriptorsJson — camelCase, matching this
/// app's own HTTP JSON convention (Program.cs's ConfigureHttpJsonOptions), so the exact same DTO
/// shape round-trips through the wire and the jsonb column without a re-shaping step.</summary>
internal static class EvaluationDescriptorsSerializer
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
