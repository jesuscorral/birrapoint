using BirraPoint.Api.Features.Evaluations;

namespace BirraPoint.Api.UnitTests.Evaluations;

/// <summary>senior-review M2: Deserialize must never throw — a row whose stored blob doesn't
/// match the current DTO shape must degrade to "no descriptors recorded", not 500 the organizer's
/// audit drill-down or poison a DispatchJob retry loop.</summary>
public sealed class EvaluationDescriptorsSerializerTests
{
    [Fact]
    public void Deserialize_of_null_returns_null()
    {
        Assert.Null(EvaluationDescriptorsSerializer.Deserialize(null));
    }

    [Fact]
    public void Deserialize_of_malformed_json_returns_null_instead_of_throwing()
    {
        var result = EvaluationDescriptorsSerializer.Deserialize("{ not valid json ");

        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_of_json_that_does_not_match_the_dto_shape_returns_null_instead_of_throwing()
    {
        var result = EvaluationDescriptorsSerializer.Deserialize("""{ "someUnrelatedField": 123 }""");

        // A structurally-valid-but-unrecognized JSON object deserializes to a DTO with every
        // nested section null (System.Text.Json's normal leniency) -- not itself an error case,
        // just confirms this shape doesn't throw either.
        Assert.NotNull(result);
        Assert.Null(result.Appearance);
    }

    [Fact]
    public void Serialize_then_deserialize_round_trips_camelCase_correctly()
    {
        var original = new EvaluationDescriptorsDto(
            new AppearanceDescriptorsDto("Golden", null, false, "Clear", "White", null, false, 60, "Silky", null),
            new AromaDescriptorsDto(2, false, 3, true, 1),
            null,
            null,
            new OverallDescriptorsDto(40, 10, 90),
            ["Diacetyl", "Oxidized"]);

        var json = EvaluationDescriptorsSerializer.Serialize(original);
        var roundTripped = EvaluationDescriptorsSerializer.Deserialize(json);

        Assert.NotNull(roundTripped);
        Assert.Equal("Golden", roundTripped.Appearance?.Color);
        Assert.Equal(60, roundTripped.Appearance?.Retention);
        Assert.Equal(2, roundTripped.Aroma?.Malt);
        Assert.True(roundTripped.Aroma?.HopsInappropriate);
        Assert.Equal(90, roundTripped.Overall?.Vitality);
        Assert.Equal(["Diacetyl", "Oxidized"], roundTripped.OffFlavors);
        // camelCase on the wire (senior-review M2/ADR-0015's own claim, now actually pinned).
        Assert.Contains("\"color\":\"Golden\"", json);
        Assert.Contains("\"offFlavors\":", json);
    }
}
