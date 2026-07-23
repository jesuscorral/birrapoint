using MediatR;

namespace BirraPoint.Api.Features.Dispatch;

/// <summary>Maps the Results &amp; dispatch endpoints (contracts/rest-api.md §Results &amp; dispatch,
/// T072-T076) — ORGANIZER-only.</summary>
public static class DispatchEndpoints
{
    public static IEndpointRouteBuilder MapDispatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Dispatch");

        group.MapGet("/results/archive", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetResultsArchiveQuery(competitionId), cancellationToken);
            if (result is null)
            {
                return Results.NotFound();
            }

            return result.ZipBytes is not null
                ? Results.File(result.ZipBytes, "application/zip", "results.zip")
                : Results.Json(new { status = result.Status }, statusCode: StatusCodes.Status202Accepted);
        })
        .WithName("GetResultsArchive")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/dispatch", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetDispatchStatusQuery(competitionId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetDispatchStatus")
        .Produces<IReadOnlyList<DispatchStatusDto>>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/dispatch/retries", async (
            Guid competitionId, RetryDispatchCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var succeeded = await sender.Send(command with { CompetitionId = competitionId }, cancellationToken);
            return succeeded ? Results.Ok() : Results.NotFound();
        })
        .WithName("RetryDispatch")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
