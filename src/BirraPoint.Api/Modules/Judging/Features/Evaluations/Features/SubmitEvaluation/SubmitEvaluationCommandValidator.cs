using BirraPoint.Api.Modules.Judging.Contracts.Dtos;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;
using FluentValidation;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.SubmitEvaluation;

/// <summary>
/// Score caps and comment-length rules only (FR-023/FR-025) — table membership/state/sequence are
/// intentionally NOT validated here (same convention as FixOrderCommandValidator: failing
/// validation for a non-member would leak this table's existence via a 400 instead of the
/// handler's 404; the real precondition gating happens in the handler and throws JudgingException).
/// </summary>
internal sealed class SubmitEvaluationCommandValidator : AbstractValidator<SubmitEvaluationCommand>
{
    public SubmitEvaluationCommandValidator()
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

        // Session 2026-09-21: Descriptors is optional (EvaluationDescriptorsDto's own doc comment) —
        // only validated, never required, when present.
        RuleFor(c => c.Descriptors!).SetValidator(new EvaluationDescriptorsDtoValidator()).When(c => c.Descriptors is not null);
        RuleFor(c => c.Feedback).MaximumLength(4000);
    }
}
