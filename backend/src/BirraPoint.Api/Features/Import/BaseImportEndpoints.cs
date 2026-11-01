using BirraPoint.Api.Features.Import.Features.ConsolidateImport;
using BirraPoint.Api.Features.Import.Features.EditImportRow;
using BirraPoint.Api.Features.Import.Features.ExcludeImportRow;
using BirraPoint.Api.Features.Import.Features.GetImport;
using BirraPoint.Api.Features.Import.Features.RevalidateImport;
using BirraPoint.Api.Features.Import.Features.UploadImport;

namespace BirraPoint.Api.Features.Import;

/// <summary>Maps every Entry Import endpoint (contracts/rest-api.md §Entry Import, T035) — ORGANIZER-only.</summary>
internal static class BaseImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}/imports")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Import");

        group.MapUploadImportEndpoint()
            .MapGetImportEndpoint()
            .MapRevalidateImportEndpoint()
            .MapEditImportRowEndpoint()
            .MapExcludeImportRowEndpoint()
            .MapConsolidateImportEndpoint();

        return endpoints;
    }
}
