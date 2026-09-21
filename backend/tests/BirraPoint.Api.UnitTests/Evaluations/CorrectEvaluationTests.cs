using BirraPoint.Api.Features.Evaluations;

namespace BirraPoint.Api.UnitTests.Evaluations;

/// <summary>
/// senior-review C1: EvaluationDescriptorsDtoValidator is shared (not duplicated, unlike
/// EvaluationScoresDto/EvaluationCommentsDto's rules — ADR-0015) between
/// SubmitEvaluationCommandValidator (exercised in SubmitEvaluationTests.cs) and this one — the
/// whole premise only actually holds if both are proven, not just the one that was written first.
/// </summary>
public sealed class CorrectEvaluationTests
{
    private const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

    private static readonly CorrectEvaluationCommandValidator Validator = new();

    private static CorrectEvaluationCommand ValidCommand(EvaluationDescriptorsDto? descriptors = null) => new(
        CompetitionId: Guid.NewGuid(),
        EvaluationId: Guid.NewGuid(),
        Scores: new EvaluationScoresDto(Aroma: 10, Appearance: 2, Flavor: 15, Mouthfeel: 4, Overall: 8),
        Comments: new EvaluationCommentsDto(
            Aroma: LongComment, Appearance: LongComment, Flavor: LongComment, Mouthfeel: LongComment, Overall: LongComment),
        Descriptors: descriptors,
        Feedback: null);

    [Fact]
    public void Absent_descriptors_are_valid()
    {
        Assert.True(Validator.Validate(ValidCommand()).IsValid);
    }

    [Fact]
    public void Out_of_range_discrete_descriptor_is_rejected_via_the_shared_validator()
    {
        var descriptors = new EvaluationDescriptorsDto(
            null, new AromaDescriptorsDto(5, false, null, false, null, false), null, null, null, null);
        var result = Validator.Validate(ValidCommand(descriptors));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Descriptors.Aroma.Malt");
    }

    [Fact]
    public void Color_outside_the_closed_list_is_rejected_via_the_shared_validator()
    {
        var descriptors = new EvaluationDescriptorsDto(
            new AppearanceDescriptorsDto("Purple", null, false, null, false, null, null, false, null, false, null, null),
            null, null, null, null, null);
        var result = Validator.Validate(ValidCommand(descriptors));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Descriptors.Appearance.Color");
    }

    [Fact]
    public void Feedback_over_4000_characters_is_rejected()
    {
        var command = ValidCommand() with { Feedback = new string('x', 4001) };

        Assert.False(Validator.Validate(command).IsValid);
    }
}
