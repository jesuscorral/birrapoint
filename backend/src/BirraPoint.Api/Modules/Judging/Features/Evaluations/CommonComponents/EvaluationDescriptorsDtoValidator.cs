using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;
using FluentValidation;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;

/// <summary>
/// Every field is optional (see EvaluationDescriptorsDto's own doc comment) — this validator only
/// rejects a field that WAS supplied but is out of range/not in its closed list, it never requires
/// presence. Shared by SubmitEvaluationCommandValidator and CorrectEvaluationCommandValidator via
/// `.SetValidator(...)` (FluentValidation's nested-validator mechanism) so the two boundaries can't
/// drift apart — unlike EvaluationScoresDto/EvaluationCommentsDto's rules, which are intentionally
/// duplicated per this codebase's existing convention, this one is new enough (Session 2026-09-21)
/// to start shared instead.
/// </summary>
internal sealed class EvaluationDescriptorsDtoValidator : AbstractValidator<EvaluationDescriptorsDto>
{
    public EvaluationDescriptorsDtoValidator()
    {
        RuleFor(d => d.Appearance!).SetValidator(new AppearanceDescriptorsDtoValidator()).When(d => d.Appearance is not null);
        RuleFor(d => d.Aroma!).SetValidator(new AromaDescriptorsDtoValidator()).When(d => d.Aroma is not null);
        RuleFor(d => d.Flavor!).SetValidator(new FlavorDescriptorsDtoValidator()).When(d => d.Flavor is not null);
        RuleFor(d => d.Mouthfeel!).SetValidator(new MouthfeelDescriptorsDtoValidator()).When(d => d.Mouthfeel is not null);
        RuleFor(d => d.Overall!).SetValidator(new OverallDescriptorsDtoValidator()).When(d => d.Overall is not null);

        RuleForEach(d => d.OffFlavors)
            .Must(term => EvaluationDescriptorCatalog.OffFlavorTerms.Contains(term, StringComparer.Ordinal))
            .WithMessage("'{PropertyValue}' is not a recognized off-flavor descriptor.")
            .When(d => d.OffFlavors is not null);

        // senior-review B2: the closed-list check above rejects an unrecognized TERM, but says
        // nothing about how many entries the list itself may hold — without this, an authenticated
        // judge could submit the same (or distinct) valid term thousands of times.
        RuleFor(d => d.OffFlavors!)
            .Must(list => list.Count <= EvaluationDescriptorCatalog.OffFlavorTerms.Count)
            .WithMessage("Too many off-flavor descriptors.")
            .When(d => d.OffFlavors is not null);
    }
}

file static class DescriptorValidatorRules
{
    /// <summary>4-stop discrete intensity slider (Nada/Bajo/Medio/Alto).</summary>
    public static IRuleBuilderOptions<T, int?> DiscreteIntensity<T>(this IRuleBuilder<T, int?> rule) =>
        rule.InclusiveBetween(0, 3).When(x => x is not null);

    /// <summary>Continuous bipolar slider (e.g. Lupulado↔Maltoso, Seco↔Dulce).</summary>
    public static IRuleBuilderOptions<T, int?> BipolarSlider<T>(this IRuleBuilder<T, int?> rule) =>
        rule.InclusiveBetween(0, 100).When(x => x is not null);
}

file sealed class AppearanceDescriptorsDtoValidator : AbstractValidator<AppearanceDescriptorsDto>
{
    // senior-review B2: every other free-text column in this codebase has an explicit max length
    // (2000 for the five section comments, 4000 for FeedbackComment) — these "Otros"/notes fields
    // are advisory annotations, not another comment box, so a shorter cap is appropriate; jsonb
    // itself has no length limit, so without this an authenticated judge could otherwise persist
    // an arbitrarily large string per field.
    private const int FreeTextMaxLength = 500;

    public AppearanceDescriptorsDtoValidator()
    {
        RuleFor(d => d.Color).Must(v => EvaluationDescriptorCatalog.ColorOptions.Contains(v!, StringComparer.Ordinal)).When(d => d.Color is not null);
        RuleFor(d => d.ColorOther).MaximumLength(FreeTextMaxLength);
        RuleFor(d => d.Clarity).Must(v => EvaluationDescriptorCatalog.ClarityOptions.Contains(v!, StringComparer.Ordinal)).When(d => d.Clarity is not null);
        RuleFor(d => d.Foam).Must(v => EvaluationDescriptorCatalog.FoamOptions.Contains(v!, StringComparer.Ordinal)).When(d => d.Foam is not null);
        RuleFor(d => d.FoamOther).MaximumLength(FreeTextMaxLength);
        RuleFor(d => d.Retention).BipolarSlider();
        RuleFor(d => d.Texture).MaximumLength(FreeTextMaxLength);
        RuleFor(d => d.Notes).MaximumLength(FreeTextMaxLength);
    }
}

file sealed class AromaDescriptorsDtoValidator : AbstractValidator<AromaDescriptorsDto>
{
    public AromaDescriptorsDtoValidator()
    {
        RuleFor(d => d.Malt).DiscreteIntensity();
        RuleFor(d => d.Hops).DiscreteIntensity();
        RuleFor(d => d.Fermentation).DiscreteIntensity();
    }
}

file sealed class FlavorDescriptorsDtoValidator : AbstractValidator<FlavorDescriptorsDto>
{
    public FlavorDescriptorsDtoValidator()
    {
        RuleFor(d => d.Malt).DiscreteIntensity();
        RuleFor(d => d.Hops).DiscreteIntensity();
        RuleFor(d => d.Bitterness).DiscreteIntensity();
        RuleFor(d => d.Fermentation).DiscreteIntensity();
        RuleFor(d => d.Balance).BipolarSlider();
        RuleFor(d => d.Finish).BipolarSlider();
    }
}

file sealed class MouthfeelDescriptorsDtoValidator : AbstractValidator<MouthfeelDescriptorsDto>
{
    public MouthfeelDescriptorsDtoValidator()
    {
        RuleFor(d => d.Body).DiscreteIntensity();
        RuleFor(d => d.Carbonation).DiscreteIntensity();
        RuleFor(d => d.AlcoholWarmth).DiscreteIntensity();
        RuleFor(d => d.Creaminess).DiscreteIntensity();
        RuleFor(d => d.Astringency).DiscreteIntensity();
        RuleFor(d => d.Notes).MaximumLength(500);
    }
}

file sealed class OverallDescriptorsDtoValidator : AbstractValidator<OverallDescriptorsDto>
{
    public OverallDescriptorsDtoValidator()
    {
        RuleFor(d => d.ClassicExample).BipolarSlider();
        RuleFor(d => d.Defects).BipolarSlider();
        RuleFor(d => d.Vitality).BipolarSlider();
    }
}
