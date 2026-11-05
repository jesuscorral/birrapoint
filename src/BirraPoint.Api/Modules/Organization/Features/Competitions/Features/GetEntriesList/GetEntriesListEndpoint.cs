using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.GetEntriesList;

internal static class GetEntriesListEndpoint
{
    internal static IEndpointRouteBuilder MapGetEntriesListEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGroup("/{competitionId:guid}/entries")
                .RequireAuthorization("ORGANIZER")
                .WithTags("Entries")
                .MapGet("/", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new ListEntriesQuery(competitionId), cancellationToken);
                    return result is null ? Results.NotFound() : Results.Ok(result);
                })
                .WithName("ListEntries")
                .Produces<IReadOnlyList<EntryDto>>()
                .Produces(StatusCodes.Status404NotFound);
        return endpointsGroup;
    }
}
