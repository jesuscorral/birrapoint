using MediatR;

namespace BirraPoint.Api.Features.Tables;

public sealed record TableAssignmentRequest(string Name, IReadOnlyList<Guid> JudgeIds, IReadOnlyList<Guid> BeerEntryIds);

/// <summary>Maps CreateTable/UpdateTable/ListTables (contracts/rest-api.md §Tables, T047) — ORGANIZER-only.</summary>
public static class TablesEndpoints
{
    public static IEndpointRouteBuilder MapTablesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}/tables")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Tables");

        group.MapPost("/", async (Guid competitionId, TableAssignmentRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new CreateTableCommand(competitionId, request.Name, request.JudgeIds, request.BeerEntryIds);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Created($"/api/v1/competitions/{competitionId}/tables/{result.Id}", result);
        })
        .WithName("CreateTable")
        .Produces<TableMutationResult>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ListTablesQuery(competitionId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ListTables")
        .Produces<IReadOnlyList<TableDto>>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{tableId:guid}", async (
            Guid competitionId, Guid tableId, TableAssignmentRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new UpdateTableCommand(competitionId, tableId, request.Name, request.JudgeIds, request.BeerEntryIds);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("UpdateTable")
        .Produces<TableMutationResult>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}/entries")
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

        return endpoints;
    }
}
