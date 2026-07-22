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
    public void IsNextInSequence_true_when_requested_id_is_the_first_not_yet_submitted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        Assert.True(SubmitEvaluationRules.IsNextInSequence([a, b, c], [a], b));
    }

    [Fact]
    public void IsNextInSequence_false_when_requested_id_is_not_the_first_not_yet_submitted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        Assert.False(SubmitEvaluationRules.IsNextInSequence([a, b, c], [], c));
    }

    [Fact]
    public void IsNextInSequence_true_for_the_first_sample_when_nothing_has_been_submitted_yet()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.True(SubmitEvaluationRules.IsNextInSequence([a, b], [], a));
    }

    [Fact]
    public void IsNextInSequence_true_for_the_last_sample_when_all_but_the_last_are_submitted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        Assert.True(SubmitEvaluationRules.IsNextInSequence([a, b, c], [a, b], c));
    }

    [Fact]
    public void IsNextInSequence_false_when_every_sample_has_already_been_submitted()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.False(SubmitEvaluationRules.IsNextInSequence([a, b], [a, b], a));
    }

    [Fact]
    public void IsNextInSequence_false_when_the_requested_id_is_not_in_the_ordered_list_at_all()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        Assert.False(SubmitEvaluationRules.IsNextInSequence([a, b], [], stranger));
    }

    // ---- CanSubmitInState -------------------------------------------------------------------------

    [Fact]
    public void CanSubmitInState_true_only_for_InEvaluation()
    {
        Assert.True(SubmitEvaluationRules.CanSubmitInState(CompetitionState.InEvaluation));
    }

    [Theory]
    [InlineData(CompetitionState.Draft)]
    [InlineData(CompetitionState.Active)]
    [InlineData(CompetitionState.Finalized)]
    public void CanSubmitInState_false_outside_InEvaluation(CompetitionState state)
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
            Aroma: LongComment, Appearance: LongComment, Flavor: LongComment, Mouthfeel: LongComment, Overall: LongComment));

    [Fact]
    public void Command_with_valid_scores_and_comments_is_valid()
    {
        Assert.True(Validator.Validate(ValidCommand()).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void Aroma_score_accepted_at_its_bounds(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Aroma = score } };
        Assert.True(Validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    public void Aroma_score_rejected_outside_its_0_to_12_cap(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Aroma = score } };
        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Scores.Aroma");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Appearance_score_accepted_at_its_bounds(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Appearance = score } };
        Assert.True(Validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Appearance_score_rejected_outside_its_0_to_3_cap(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Appearance = score } };
        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Scores.Appearance");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    public void Flavor_score_accepted_at_its_bounds(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Flavor = score } };
        Assert.True(Validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void Flavor_score_rejected_outside_its_0_to_20_cap(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Flavor = score } };
        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Scores.Flavor");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Mouthfeel_score_accepted_at_its_bounds(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Mouthfeel = score } };
        Assert.True(Validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Mouthfeel_score_rejected_outside_its_0_to_5_cap(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Mouthfeel = score } };
        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Scores.Mouthfeel");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Overall_score_accepted_at_its_bounds(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Overall = score } };
        Assert.True(Validator.Validate(command).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void Overall_score_rejected_outside_its_0_to_10_cap(int score)
    {
        var command = ValidCommand() with { Scores = ValidCommand().Scores with { Overall = score } };
        var result = Validator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Scores.Overall");
    }

    // ---- SubmitEvaluationCommandValidator: comment length (FR-025) -----------------------------

    [Fact]
    public void Comment_of_exactly_20_characters_is_accepted()
    {
        var twentyChars = new string('x', 20);
        var command = ValidCommand() with { Comments = ValidCommand().Comments with { Aroma = twentyChars } };

        Assert.True(Validator.Validate(command).IsValid);
    }

    [Fact]
    public void Comment_under_20_characters_is_rejected()
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
    public void Every_comment_section_enforces_the_same_minimum_length(string section)
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
}
