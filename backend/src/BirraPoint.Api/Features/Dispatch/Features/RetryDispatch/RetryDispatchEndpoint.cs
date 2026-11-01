using MediatR;

namespace BirraPoint.Api.Features.Dispatch.Features.RetryDispatch;

internal static class RetryDispatchEndpoint
{
    internal static IEndpointRouteBuilder MapRetryDispatchEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/dispatch/retries", async (
            Guid competitionId, RetryDispatchCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var succeeded = await sender.Send(command with { CompetitionId = competitionId }, cancellationToken);
            return succeeded ? Results.Ok() : Results.NotFound();
        })
        .WithName("RetryDispatch")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
