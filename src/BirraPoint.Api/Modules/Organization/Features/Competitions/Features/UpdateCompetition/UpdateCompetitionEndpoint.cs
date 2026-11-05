using BirraPoint.Api.Modules.Organization.Features.Competitions.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.UpdateCompetition;

internal static class UpdateCompetitionEndpoint
{
    internal static IEndpointRouteBuilder MapUpdateCompetitionEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPut("/{id:guid}", async (Guid id, UpdateCompetitionCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command with { Id = id }, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("UpdateCompetition")
        .Produces<CompetitionDetailDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
