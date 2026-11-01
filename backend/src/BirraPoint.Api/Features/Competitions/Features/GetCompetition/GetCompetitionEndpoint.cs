using BirraPoint.Api.Features.Competitions.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Competitions.Features.GetCompetition;

internal static class GetCompetitionEndpoint
{
    internal static IEndpointRouteBuilder MapGetCompetitionEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetCompetitionQuery(id), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetCompetition")
        .Produces<CompetitionDetailDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
