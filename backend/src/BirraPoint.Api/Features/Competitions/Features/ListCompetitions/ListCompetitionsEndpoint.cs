using MediatR;

namespace BirraPoint.Api.Features.Competitions.Features.ListCompetitions;

internal static class ListCompetitionsEndpoint
{
    internal static IEndpointRouteBuilder MapListCompetitionsEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new ListCompetitionsQuery(), cancellationToken)))
        .WithName("ListCompetitions")
        .Produces<IReadOnlyList<CompetitionSummaryDto>>();

        return endpointsGroup;
    }
}
