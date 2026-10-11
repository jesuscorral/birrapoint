using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Evaluations;

namespace BirraPoint.Api.UnitTests.Evaluations;

/// <summary>
/// T055: pure SubmitEvaluation rules (FR-022/FR-023/FR-025) — strictly-sequential sample gating
/// and the InEvaluation-only state gate — exercised directly (same "pure rule class beside the
/// DB-touching handler" pattern already used by Features/TastingOrder/TastingOrderRules,
/// Features/Tables/BosFlagRules and CoiDetector), plus the FluentValidation score-cap/comment-
/// length rules SubmitEvaluationCommandValidator enforces at the wire boundary. The full
/// precondition chain (state/order-fixed/table-closed/out-of-sequence → 409, idempotent replay)
/// needs a real transaction against Postgres — covered by SubmitEvaluationApiTests.cs (T056).
/// </summary>
public sealed class SubmitEvaluationTests
{
    // ---- IsNextInSequence -----------------------------------------------------------------------

    [Fact]
    public void IsNextInSequenceTrueWhenRequestedIdIsTheFirstNotYetSubmitted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        Assert.True(SubmitEvaluationRules.IsNextInSequence([a, b, c], [a], b));
    }

    [Fact]
    public void IsNextInSequenceFalseWhenRequestedIdIsNotTheFirstNotYetSubmitted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        Assert.False(SubmitEvaluationRules.IsNextInSequence([a, b, c], [], c));
    }

    [Fact]
    public void IsNextInSequenceTrueForTheFirstSampleWhenNothingHasBeenSubmittedYet()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.True(SubmitEvaluationRules.IsNextInSequence([a, b], [], a));
    }

    [Fact]
    public void IsNextInSequenceTrueForTheLastSampleWhenAllButTheLastAreSubmitted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        Assert.True(SubmitEvaluationRules.IsNextInSequence([a, b, c], [a, b], c));
    }

    [Fact]
    public void IsNextInSequenceFalseWhenEverySampleHasAlreadyBeenSubmitted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.False(SubmitEvaluationRules.IsNextInSequence([a, b], [a, b], a));
    }

    [Fact]
    public void IsNextInSequenceFalseWhenTheRequestedIdIsNotInTheOrderedListAtAll()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        Assert.False(SubmitEvaluationRules.IsNextInSequence([a, b], [], stranger));
    }

    // ---- CanSubmitInState -------------------------------------------------------------------------

    [Fact]
    public void CanSubmitInStateTrueOnlyForInEvaluation()
    {
        Assert.True(SubmitEvaluationRules.CanSubmitInState(CompetitionState.InEvaluation));
    }

    [Theory]
    [InlineData(CompetitionState.Draft)]
    [InlineData(CompetitionState.Active)]
    [InlineData(CompetitionState.Finalized)]
    public void CanSubmitInStateFalseOutsideInEvaluation(CompetitionState state)
    {
        Assert.False(SubmitEvaluationRules.CanSubmitInState(state));
    }

    // ---- SubmitEvaluationCommandValidator: score caps (FR-023/FR-024) --------------------------

    private const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

    private static readonly SubmitEvaluationCommandValidator Validator = new();

    private static SubmitEvaluationCommand ValidCommand() => new(
        TableId: Guid.NewGuid(),
        BeerEntryId: Guid.NewGuid(),
        Scores: new EvaluationScoresDto(Aroma: 10, Appearance: 2, Flavor: 15, Mouthfeel: 4, Overall: 8),
        Comments: new EvaluationCommentsDto(
            Aroma: LongComment, Appearance: LongComment, Flavor: LongComment, Mouthfeel: LongComment, Overall: LongComment),
        Descriptors: null,
        Feedback: null);

    [Fact]
    public void CommandWithValidScoresAndCommentsIsValid()
    {
        Assert.True(Validator.Validate(ValidCommand()).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void AromaScoreAcceptedAtItsBounds(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Aroma = score } };
        Assert.True(Validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    public void AromaScoreRejectedOutsideIts0To12Cap(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Aroma = score } };
        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Scores.Aroma");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void AppearanceScoreAcceptedAtItsBounds(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Appearance = score } };
        Assert.True(Validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void AppearanceScoreRejectedOutsideIts0To3Cap(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Appearance = score } };
        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Scores.Appearance");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void FlavorScoreAcceptedAtItsBounds(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Flavor = score } };
        Assert.True(Validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void FlavorScoreRejectedOutsideIts0To20Cap(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Flavor = score } };
        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Scores.Flavor");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void MouthfeelScoreAcceptedAtItsBounds(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Mouthfeel = score } };
        Assert.True(Validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void MouthfeelScoreRejectedOutsideIts0To5Cap(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Mouthfeel = score } };
        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Scores.Mouthfeel");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void OverallScoreAcceptedAtItsBounds(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Overall = score } };
        Assert.True(Validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void OverallScoreRejectedOutsideIts0To10Cap(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Overall = score } };
        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Scores.Overall");
    }

    // ---- SubmitEvaluationCommandValidator: comment length (FR-025) -----------------------------

    [Fact]
    public void CommentOfExactly20CharactersIsAccepted()
    {
        var twentyChars = new string('x', 20);
        var command = ValidCommand() with { Comments = ValidCommand().Comments with { Aroma = twentyChars } };

        Assert.True(Validator.Validate(command).IsValid);
    }

    [Fact]
    public void CommentUnder20CharactersIsRejected()
    {
        var nineteenChars = new string('x', 19);
        var command = ValidCommand() with { Comments = ValidCommand().Comments with { Aroma = nineteenChars } };

        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Comments.Aroma");
    }

    [Theory]
    [InlineData("Appearance")]
    [InlineData("Flavor")]
    [InlineData("Mouthfeel")]
    [InlineData("Overall")]
    public void EveryCommentSectionEnforcesTheSameMinimumLength(string section)
    {
        var tooShort = "short";
        var comments = ValidCommand().Comments;
        comments = section switch
        {
            "Appearance" => comments with { Appearance = tooShort },
            "Flavor" => comments with { Flavor = tooShort },
            "Mouthfeel" => comments with { Mouthfeel = tooShort },
            "Overall" => comments with { Overall = tooShort },
            _ => throw new ArgumentOutOfRangeException(nameof(section)),
        };

        var result = Validator.Validate(ValidCommand() with { Comments = comments });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == $"Comments.{section}");
    }

    // ---- SubmitEvaluationCommandValidator: descriptors (Session 2026-09-21) — every field is
    // optional; these only assert that a SUPPLIED value is range/catalog-checked. -----------------

    [Fact]
    public void AbsentDescriptorsAreValid()
    {
        Assert.True(Validator.Validate(ValidCommand() with { Descriptors = null }).IsValid);
    }

    [Fact]
    public void EmptyDescriptorsObjectIsValid()
    {
        var descriptors = new EvaluationDescriptorsDto(null, null, null, null, null, null);
        Assert.True(Validator.Validate(ValidCommand() with { Descriptors = descriptors }).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void DiscreteIntensitySliderAcceptedAtItsBounds(int value)
    {
        var descriptors = new EvaluationDescriptorsDto(
            null, new AromaDescriptorsDto(value, false, null, false, null, false), null, null, null, null);
        Assert.True(Validator.Validate(ValidCommand() with { Descriptors = descriptors }).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void DiscreteIntensitySliderRejectedOutside0To3(int value)
    {
        var descriptors = new EvaluationDescriptorsDto(
            null, new AromaDescriptorsDto(value, false, null, false, null, false), null, null, null, null);
        var result = Validator.Validate(ValidCommand() with { Descriptors = descriptors });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Descriptors.Aroma.Malt");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void BipolarSliderAcceptedAtItsBounds(int value)
    {
        var descriptors = new EvaluationDescriptorsDto(
            null, null, null, null, new OverallDescriptorsDto(value, false, null, false, null, false), null);
        Assert.True(Validator.Validate(ValidCommand() with { Descriptors = descriptors }).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void BipolarSliderRejectedOutside0To100(int value)
    {
        var descriptors = new EvaluationDescriptorsDto(
            null, null, null, null, new OverallDescriptorsDto(value, false, null, false, null, false), null);
        var result = Validator.Validate(ValidCommand() with { Descriptors = descriptors });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Descriptors.Overall.ClassicExample");
    }

    [Fact]
    public void ColorOutsideTheClosedListIsRejected()
    {
        var descriptors = new EvaluationDescriptorsDto(
            new AppearanceDescriptorsDto("Purple", null, false, null, false, null, null, false, null, false, null, null),
            null, null, null, null, null);
        var result = Validator.Validate(ValidCommand() with { Descriptors = descriptors });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Descriptors.Appearance.Color");
    }

    [Theory]
    [InlineData("Yellow")]
    [InlineData("Other")]
    public void ColorInTheClosedListIsAccepted(string color)
    {
        var descriptors = new EvaluationDescriptorsDto(
            new AppearanceDescriptorsDto(color, null, false, null, false, null, null, false, null, false, null, null),
            null, null, null, null, null);
        Assert.True(Validator.Validate(ValidCommand() with { Descriptors = descriptors }).IsValid);
    }

    [Fact]
    public void OffFlavorTermOutsideTheClosedListIsRejected()
    {
        var descriptors = new EvaluationDescriptorsDto(null, null, null, null, null, ["NotARealDescriptor"]);
        var result = Validator.Validate(ValidCommand() with { Descriptors = descriptors });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void OffFlavorTermsFromTheClosedListAreAccepted()
    {
        var descriptors = new EvaluationDescriptorsDto(null, null, null, null, null, ["Diacetyl", "Oxidized"]);
        Assert.True(Validator.Validate(ValidCommand() with { Descriptors = descriptors }).IsValid);
    }

    [Fact]
    public void OffFlavorListLongerThanTheClosedListItselfIsRejected()
    {
        // Every entry is individually valid (a repeated real term) — this exercises the *count*
        // cap, not the closed-list membership check (senior-review B2).
        var tooMany = Enumerable.Repeat("Diacetyl", EvaluationDescriptorCatalog.OffFlavorTerms.Count + 1).ToList();
        var descriptors = new EvaluationDescriptorsDto(null, null, null, null, null, tooMany);
        var result = Validator.Validate(ValidCommand() with { Descriptors = descriptors });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void FreeTextDescriptorFieldOver500CharactersIsRejected()
    {
        // senior-review B2: unbounded free text was the one input path that escaped both EF and
        // FluentValidation length checks — Texture is one of five such fields (ColorOther,
        // FoamOther, Notes on both Appearance and Mouthfeel are the others, same 500 cap).
        var tooLong = new string('x', 501);
        var descriptors = new EvaluationDescriptorsDto(
            new AppearanceDescriptorsDto(null, null, false, null, false, null, null, false, null, false, tooLong, null),
            null, null, null, null, null);
        var result = Validator.Validate(ValidCommand() with { Descriptors = descriptors });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Descriptors.Appearance.Texture");
    }

    [Fact]
    public void FreeTextDescriptorFieldAtExactly500CharactersIsAccepted()
    {
        var atLimit = new string('x', 500);
        var descriptors = new EvaluationDescriptorsDto(
            new AppearanceDescriptorsDto(null, null, false, null, false, null, null, false, null, false, atLimit, null),
            null, null, null, null, null);
        Assert.True(Validator.Validate(ValidCommand() with { Descriptors = descriptors }).IsValid);
    }

    [Fact]
    public void FeedbackOver4000CharactersIsRejected()
    {
        var command = ValidCommand() with { Feedback = new string('x', 4001) };

        Assert.False(Validator.Validate(command).IsValid);
    }
}
