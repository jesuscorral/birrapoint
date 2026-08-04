using MediatR;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Maps every Judge Roster Import endpoint (contracts/rest-api.md §Judge Roster Import, US14/T115) — ORGANIZER-only.</summary>
public static class JudgeImportEndpoints
{
    public static IEndpointRouteBuilder MapJudgeImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}/judge-imports")
            .RequireAuthorization("ORGANIZER")
            .WithTags("JudgeImport");

        group.MapPost("/", async (Guid competitionId, IFormFile file, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new UploadJudgeImportCommand(competitionId, file), cancellationToken);
            return result is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/competitions/{competitionId}/judge-imports/{result.ImportId}", result);
        })
        .WithName("UploadJudgeImport")
        .DisableAntiforgery()
        .Produces<JudgeImportBatchDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{importId:guid}", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetJudgeImportQuery(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetJudgeImport")
        .Produces<JudgeImportBatchDto>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{importId:guid}/rows/{rowNumber:int}", async (
            Guid competitionId, Guid importId, int rowNumber, EditJudgeImportRowRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new EditJudgeImportRowCommand(competitionId, importId, rowNumber, request);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("EditJudgeImportRow")
        .Produces<JudgeImportRowDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{importId:guid}/rows/{rowNumber:int}/exclude", async (
            Guid competitionId, Guid importId, int rowNumber, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ExcludeJudgeImportRowCommand(competitionId, importId, rowNumber), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ExcludeJudgeImportRow")
        .Produces<JudgeImportRowDto>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{importId:guid}/consolidate", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ConsolidateJudgeImportCommand(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ConsolidateJudgeImport")
        .Produces<ConsolidateJudgeImportResult>()
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
