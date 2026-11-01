using BirraPoint.Api.Features.Import.Features.ConsolidateBeerImport;
using BirraPoint.Api.Features.Import.Features.ConsolidateJudgeImport;
using BirraPoint.Api.Features.Import.Features.EditBeerImportRow;
using BirraPoint.Api.Features.Import.Features.EditJudgeImportRow;
using BirraPoint.Api.Features.Import.Features.ExcludeBeerImportRow;
using BirraPoint.Api.Features.Import.Features.ExcludeJudgeImportRow;
using BirraPoint.Api.Features.Import.Features.GetBeerImport;
using BirraPoint.Api.Features.Import.Features.GetJudgeImport;
using BirraPoint.Api.Features.Import.Features.RevalidateBeerImport;
using BirraPoint.Api.Features.Import.Features.UploadBeerImport;
using BirraPoint.Api.Features.Import.Features.UploadJudgeImport;

namespace BirraPoint.Api.Features.Import;

/// <summary>Maps every import endpoint — beer entries (contracts/rest-api.md §Beer Entry Import)
/// and the judge roster (§Judge Roster Import, US14) — ORGANIZER-only.</summary>
internal static class BaseImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var beerGroup = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}/imports/beers")
            .RequireAuthorization("ORGANIZER")
            .WithTags("BeerImport");

        beerGroup.MapUploadBeerImportEndpoint()
            .MapGetBeerImportEndpoint()
            .MapRevalidateBeerImportEndpoint()
            .MapEditBeerImportRowEndpoint()
            .MapExcludeBeerImportRowEndpoint()
            .MapConsolidateBeerImportEndpoint();

        var judgeGroup = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}/imports/judges")
            .RequireAuthorization("ORGANIZER")
            .WithTags("JudgeImport");

        judgeGroup.MapUploadJudgeImportEndpoint()
            .MapGetJudgeImportEndpoint()
            .MapEditJudgeImportRowEndpoint()
            .MapExcludeJudgeImportRowEndpoint()
            .MapConsolidateJudgeImportEndpoint();

        return endpoints;
    }
}
