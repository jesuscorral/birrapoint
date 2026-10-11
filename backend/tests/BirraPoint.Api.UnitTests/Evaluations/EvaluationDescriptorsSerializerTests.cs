using BirraPoint.Api.Features.Evaluations;

namespace BirraPoint.Api.UnitTests.Evaluations;

/// <summary>senior-review M2: Deserialize must never throw — a row whose stored blob doesn't
/// match the current DTO shape must degrade to "no descriptors recorded", not 500 the organizer's
/// audit drill-down or poison a DispatchJob retry loop.</summary>
public sealed class EvaluationDescriptorsSerializerTests
{
    [Fact]
    public void DeserializeOfNullReturnsNull()
    {
        Assert.Null(EvaluationDescriptorsSerializer.Deserialize(null));
    }

    [Fact]
    public void DeserializeOfMalformedJsonReturnsNullInsteadOfThrowing()
    {
        var result = EvaluationDescriptorsSerializer.Deserialize("{ not valid json ");

        Assert.Null(result);
    }

    [Fact]
    public void DeserializeOfJsonThatDoesNotMatchTheDtoShapeReturnsNullInsteadOfThrowing()
    {
        var result = EvaluationDescriptorsSerializer.Deserialize("""{ "someUnrelatedField": 123 }""");

        // A structurally-valid-but-unrecognized JSON object deserializes to a DTO with every
        // nested section null (System.Text.Json's normal leniency) -- not itself an error case,
        // just confirms this shape doesn't throw either.
        Assert.NotNull(result);
        Assert.Null(result.Appearance);
    }

    [Fact]
    public void SerializeThenDeserializeRoundTripsCamelCaseCorrectly()
    {
        var original = new EvaluationDescriptorsDto(
            new AppearanceDescriptorsDto("Golden", null, false, "Clear", false, "White", null, false, 60, false, "Silky", null),
            new AromaDescriptorsDto(2, false, 3, true, 1, false),
            null,
            null,
            new OverallDescriptorsDto(40, false, 10, false, 90, false),
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
        Assert.Contains("\"color\":\"Golden\"", json, StringComparison.Ordinal);
        Assert.Contains("\"offFlavors\":", json, StringComparison.Ordinal);
    }
}
