using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.ChangeCompetitionState;

internal static class ChangeCompetitionStateEndpoint
{
    internal static IEndpointRouteBuilder MapChangeCompetitionStateEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{id:guid}/state", async (Guid id, ChangeCompetitionStateCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command with { Id = id }, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ChangeCompetitionState")
        .Produces<ChangeCompetitionStateResult>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
