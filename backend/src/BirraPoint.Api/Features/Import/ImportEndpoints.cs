using MediatR;
using Microsoft.AspNetCore.Http;

namespace BirraPoint.Api.Features.Import;

public sealed record ResolveRowRequest(string Action, string? StyleCode);

/// <summary>Maps every Entry Import endpoint (contracts/rest-api.md §Entry Import, T035) — ORGANIZER-only.</summary>
public static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}/imports")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Import");

        group.MapPost("/", async (Guid competitionId, IFormFile file, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new UploadImportCommand(competitionId, file), cancellationToken);
            return result is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/competitions/{competitionId}/imports/{result.ImportId}", result);
        })
        .WithName("UploadImport")
        .DisableAntiforgery()
        .Produces<ImportBatchDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{importId:guid}", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetImportQuery(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetImport")
        .Produces<ImportBatchDto>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{importId:guid}/rows/{rowNumber:int}", async (
            Guid competitionId, Guid importId, int rowNumber, ResolveRowRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new ResolveRowCommand(competitionId, importId, rowNumber, request.Action, request.StyleCode);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ResolveImportRow")
        .Produces<ImportRowDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{importId:guid}/consolidate", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ConsolidateImportCommand(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ConsolidateImport")
        .Produces<ConsolidateImportResult>()
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
