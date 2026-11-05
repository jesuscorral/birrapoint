using BirraPoint.Api.Modules.Catalog.Contracts;
using FluentValidation;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.SetCompetitionCategories;

internal sealed class SetCompetitionCategoriesCommandValidator : AbstractValidator<SetCompetitionCategoriesCommand>
{
    public SetCompetitionCategoriesCommandValidator(IStyleCatalog styleCatalog)
    {
        // Cascade(Stop): the DB-backed existence check in DependentRules must never run once a
        // cheaper sync check has already failed (mirrors ResolveRowCommandValidator/
        // UpdateJudgeEmailCommandValidator's DependentRules usage for the same reason).
        RuleFor(c => c.Categories)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("At least one category is required.")
            .Must(categories => categories.Any(category => category.StyleCodes.Count > 0))
            .WithMessage("At least one style must be assigned to a category.")
            .Must(HaveUniqueNames)
            .WithMessage("Category names must be unique.")
            .Must(HaveNoDuplicateStyleCodes)
            .WithMessage("A style code may not be assigned to more than one category.")
            .DependentRules(() =>
            {
                RuleFor(c => c.Categories)
                    .MustAsync((categories, cancellationToken) => AllStyleCodesExistAsync(styleCatalog, categories, cancellationToken))
                    .WithMessage("One or more style codes do not exist in the BJCP 2021 catalog.");
            });

        RuleForEach(c => c.Categories).ChildRules(category =>
        {
            category.RuleFor(item => item.Name).NotEmpty().MaximumLength(100);
        });
    }

    private static bool HaveUniqueNames(IReadOnlyList<SetCategoryItem> categories) =>
        categories.Select(category => category.Name).Distinct(StringComparer.Ordinal).Count() == categories.Count;

    private static bool HaveNoDuplicateStyleCodes(IReadOnlyList<SetCategoryItem> categories)
    {
        var allCodes = categories.SelectMany(category => category.StyleCodes).ToList();
        return allCodes.Distinct(StringComparer.Ordinal).Count() == allCodes.Count;
    }

    private static async Task<bool> AllStyleCodesExistAsync(
        IStyleCatalog styleCatalog, IReadOnlyList<SetCategoryItem> categories, CancellationToken cancellationToken)
    {
        var codes = categories.SelectMany(category => category.StyleCodes).Distinct(StringComparer.Ordinal).ToList();
        if (codes.Count == 0)
        {
            return true;
        }

        var existing = await styleCatalog.GetByCodesAsync(codes, cancellationToken);
        return existing.Count == codes.Count;
    }
}
