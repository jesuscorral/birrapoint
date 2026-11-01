using BirraPoint.Api.Modules.Organization.Features.Competitions.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.CreateCompetition;

internal static class CreateCompetitionEndpoint
{
    internal static IEndpointRouteBuilder MapCreateCompetitionEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/", async (CreateCompetitionCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command, cancellationToken);
            return Results.Created($"/api/v1/competitions/{result.Id}", result);
        })
        .WithName("CreateCompetition")
        .Produces<CompetitionDetailDto>(StatusCodes.Status201Created);

        return endpointsGroup;
    }
}
