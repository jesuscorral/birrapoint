using BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.AdjustEvaluation;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.CloseTable;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.CorrectEvaluation;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.GetMyDiscrepancies;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.SubmitEvaluation;
using BirraPoint.Common.Security;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations;

internal static class BaseEvaluationsEndpoints
{
    public static IEndpointRouteBuilder MapEvaluationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/me/tables/{tableId:guid}/evaluations")
            .RequireAuthorization(Policies.Judge)
            .WithTags("Evaluations");

        group.MapSubmitEvaluationEndpoint()
            .MapAdjustEvaluationEndpoint();

        // Different path shape than the /evaluations group above — a sibling resource under the
        // same /me/tables/{tableId} prefix, same JUDGE-only convention (mirrors how
        // Modules/Judging/Features/Tables/BaseTablesEndpoints.cs maps a second, differently-shaped group from
        // the same file).
        var closeGroup = endpoints.MapGroup("/api/v1/me/tables/{tableId:guid}")
            .RequireAuthorization(Policies.Judge)
            .WithTags("Evaluations");

        closeGroup.MapCloseTableEndpoint()
            .MapGetMyDiscrepanciesEndpoint();

        var correctionGroup = endpoints.MapGroup("/api/v1/competitions/{id:guid}/evaluations")
            .RequireAuthorization(Policies.Organizer)
            .WithTags("Evaluations");

        correctionGroup.MapCorrectEvaluationEndpoint();

        return endpoints;
    }
}
