using BirraPoint.Api.Modules.Organization.Features.Competitions.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.GetCompetitionCategories;

internal static class GetCompetitionCategoriesEndpoint
{
    internal static IEndpointRouteBuilder MapGetCompetitionCategoriesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/{id:guid}/categories", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetCompetitionCategoriesQuery(id), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetCompetitionCategories")
        .Produces<CompetitionCategoriesDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
