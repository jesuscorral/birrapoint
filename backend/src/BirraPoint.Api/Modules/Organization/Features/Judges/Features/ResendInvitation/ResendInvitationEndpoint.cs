using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Judges.Features.ResendInvitation;

internal static class ResendInvitationEndpoint
{
    internal static IEndpointRouteBuilder MapResendInvitationEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{judgeId:guid}/invitation", async (Guid competitionId, Guid judgeId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ResendInvitationCommand(competitionId, judgeId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ResendInvitation")
        .Produces<ResendInvitationResult>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
