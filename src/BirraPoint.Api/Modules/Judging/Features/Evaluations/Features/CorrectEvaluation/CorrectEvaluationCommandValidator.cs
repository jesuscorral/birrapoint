using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;
using FluentValidation;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.CorrectEvaluation;

/// <summary>
/// Same score caps (FR-023/FR-024) and comment-length rule (FR-025) as
/// SubmitEvaluationCommandValidator, sourced from the same SubmitEvaluationRules constants so the
/// two boundaries can never drift apart — duplicated rather than shared because the two commands
/// have unrelated shapes (this one also carries CompetitionId/EvaluationId).
/// </summary>
internal sealed class CorrectEvaluationCommandValidator : AbstractValidator<CorrectEvaluationCommand>
{
    public CorrectEvaluationCommandValidator()
    {
        RuleFor(c => c.Scores).NotNull();
        RuleFor(c => c.Comments).NotNull();

        When(c => c.Scores is not null, () =>
        {
            RuleFor(c => c.Scores.Aroma).InclusiveBetween(0, SubmitEvaluationRules.AromaMax);
            RuleFor(c => c.Scores.Appearance).InclusiveBetween(0, SubmitEvaluationRules.AppearanceMax);
            RuleFor(c => c.Scores.Flavor).InclusiveBetween(0, SubmitEvaluationRules.FlavorMax);
            RuleFor(c => c.Scores.Mouthfeel).InclusiveBetween(0, SubmitEvaluationRules.MouthfeelMax);
            RuleFor(c => c.Scores.Overall).InclusiveBetween(0, SubmitEvaluationRules.OverallMax);
        });

        When(c => c.Comments is not null, () =>
        {
            RuleFor(c => c.Comments.Aroma).NotEmpty().MinimumLength(SubmitEvaluationRules.MinCommentLength);
            RuleFor(c => c.Comments.Appearance).NotEmpty().MinimumLength(SubmitEvaluationRules.MinCommentLength);
            RuleFor(c => c.Comments.Flavor).NotEmpty().MinimumLength(SubmitEvaluationRules.MinCommentLength);
            RuleFor(c => c.Comments.Mouthfeel).NotEmpty().MinimumLength(SubmitEvaluationRules.MinCommentLength);
            RuleFor(c => c.Comments.Overall).NotEmpty().MinimumLength(SubmitEvaluationRules.MinCommentLength);
        });

        RuleFor(c => c.Descriptors!).SetValidator(new EvaluationDescriptorsDtoValidator()).When(c => c.Descriptors is not null);
        RuleFor(c => c.Feedback).MaximumLength(4000);
    }
}
