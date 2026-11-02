using BirraPoint.Api.Modules.Organization.Features.Judges.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Judges.Features.UpdateJudgeEmail;

internal static class UpdateJudgeEmailEndpoint
{
    internal sealed record UpdateJudgeEmailRequest(string Email);

    internal static IEndpointRouteBuilder MapUpdateJudgeEmailEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPut("/{judgeId:guid}", async (
            Guid competitionId, Guid judgeId, UpdateJudgeEmailRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new UpdateJudgeEmailCommand(competitionId, judgeId, request.Email), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("UpdateJudgeEmail")
        .Produces<JudgeProfileDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        return endpointsGroup;
    }
}
