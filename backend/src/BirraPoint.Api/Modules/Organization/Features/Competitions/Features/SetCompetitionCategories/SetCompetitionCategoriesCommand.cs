using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Features.Competitions.CommonComponents;
using BirraPoint.Api.Modules.Organization.Features.Competitions.CommonComponents.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.SetCompetitionCategories;

internal sealed record SetCategoryItem(string Name, int DisplayOrder, IReadOnlyList<string> StyleCodes);

/// <summary>
/// Full-replace PUT (matches the existing "PUT is a full replace" convention used by
/// UpdateCompetition) — returns null when not found or not owned by the caller (endpoint maps
/// that to a plain 404).
/// </summary>
internal sealed record SetCompetitionCategoriesCommand(Guid CompetitionId, IReadOnlyList<SetCategoryItem> Categories)
    : IRequest<CompetitionCategoriesDto?>;

internal sealed class SetCompetitionCategoriesCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
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
