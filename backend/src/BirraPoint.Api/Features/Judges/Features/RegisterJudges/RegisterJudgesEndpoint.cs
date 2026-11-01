using MediatR;

namespace BirraPoint.Api.Features.Judges.Features.RegisterJudges;

internal static class RegisterJudgesEndpoint
{
    internal sealed record RegisterJudgesRequest(IReadOnlyList<string> Emails);

    internal static IEndpointRouteBuilder MapRegisterJudgesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/", async (Guid competitionId, RegisterJudgesRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new RegisterJudgesCommand(competitionId, request.Emails), cancellationToken);
            return result is null ? Results.NotFound() : Results.Created($"/api/v1/competitions/{competitionId}/judges", result);
        })
        .WithName("RegisterJudges")
        .Produces<RegisterJudgesResult>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
