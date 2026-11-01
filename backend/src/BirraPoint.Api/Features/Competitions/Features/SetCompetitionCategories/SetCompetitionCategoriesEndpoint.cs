using BirraPoint.Api.Features.Competitions.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Competitions.Features.SetCompetitionCategories;

internal static class SetCompetitionCategoriesEndpoint
{
    internal static IEndpointRouteBuilder MapSetCompetitionCategoriesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPut("/{id:guid}/categories", async (Guid id, SetCompetitionCategoriesCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command with { CompetitionId = id }, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("SetCompetitionCategories")
        .Produces<CompetitionCategoriesDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
