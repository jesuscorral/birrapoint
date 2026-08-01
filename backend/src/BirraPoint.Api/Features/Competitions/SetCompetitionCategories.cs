using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Competitions;

public sealed record SetCategoryItem(string Name, int DisplayOrder, IReadOnlyList<string> StyleCodes);

/// <summary>
/// Full-replace PUT (matches the existing "PUT is a full replace" convention used by
/// UpdateCompetition) — returns null when not found or not owned by the caller (endpoint maps
/// that to a plain 404).
/// </summary>
public sealed record SetCompetitionCategoriesCommand(Guid CompetitionId, IReadOnlyList<SetCategoryItem> Categories)
    : IRequest<CompetitionCategoriesDto?>;

public sealed class SetCompetitionCategoriesCommandValidator : AbstractValidator<SetCompetitionCategoriesCommand>
{
    public SetCompetitionCategoriesCommandValidator(AppDbContext dbContext)
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
                    .MustAsync((categories, cancellationToken) => AllStyleCodesExistAsync(dbContext, categories, cancellationToken))
                    .WithMessage("One or more style codes do not exist in the BJCP 2021 catalog.");
            });

        RuleForEach(c => c.Categories).ChildRules(category =>
        {
            category.RuleFor(item => item.Name).NotEmpty().MaximumLength(100);
        });
    }

    private static bool HaveUniqueNames(IReadOnlyList<SetCategoryItem> categories) =>
        categories.Select(category => category.Name).Distinct().Count() == categories.Count;

    private static bool HaveNoDuplicateStyleCodes(IReadOnlyList<SetCategoryItem> categories)
    {
        var allCodes = categories.SelectMany(category => category.StyleCodes).ToList();
        return allCodes.Distinct().Count() == allCodes.Count;
    }

    private static async Task<bool> AllStyleCodesExistAsync(
        AppDbContext dbContext, IReadOnlyList<SetCategoryItem> categories, CancellationToken cancellationToken)
    {
        var codes = categories.SelectMany(category => category.StyleCodes).Distinct().ToList();
        if (codes.Count == 0)
        {
            return true;
        }

        var existingCount = await dbContext.BjcpStyles.CountAsync(style => codes.Contains(style.Code), cancellationToken);
        return existingCount == codes.Count;
    }
}

public sealed class SetCompetitionCategoriesCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<SetCompetitionCategoriesCommand, CompetitionCategoriesDto?>
{
    public async Task<CompetitionCategoriesDto?> Handle(SetCompetitionCategoriesCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        if (competition.State is not (CompetitionState.Draft or CompetitionState.Active))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                $"Categories cannot be edited while the competition is in state {competition.State}.");
        }

        var existingCategories = await dbContext.CompetitionCategories
            .Where(category => category.CompetitionId == competition.Id)
            .ToListAsync(cancellationToken);

        dbContext.CompetitionCategories.RemoveRange(existingCategories);

        foreach (var item in request.Categories)
        {
            var category = new CompetitionCategory
            {
                CompetitionId = competition.Id,
                Name = item.Name,
                DisplayOrder = item.DisplayOrder,
            };

            foreach (var styleCode in item.StyleCodes)
            {
                category.Styles.Add(new CompetitionCategoryStyle
                {
                    CompetitionCategoryId = category.Id,
                    StyleCode = styleCode,
                    CompetitionId = competition.Id,
                });
            }

            dbContext.CompetitionCategories.Add(category);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await CompetitionCategoryProjection.LoadAsync(dbContext, competition.Id, cancellationToken);
    }
}
