using BirraPoint.Api.Modules.Organization.Features.Judges.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Judges.Features.GetJudges;

internal static class GetJudgesEndpoint
{
    internal static IEndpointRouteBuilder MapGetJudgesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetJudgesQuery(competitionId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetJudges")
        .Produces<IReadOnlyList<JudgeProfileDto>>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
