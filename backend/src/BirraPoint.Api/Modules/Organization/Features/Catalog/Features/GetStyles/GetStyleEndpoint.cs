using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Catalog.Features.GetStyles;

internal static class GetStyleEndpoint
{
    internal static IEndpointRouteBuilder MapGetStylesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new GetStylesQuery(), cancellationToken)))
            .WithName("GetStyles")

            .Produces<IReadOnlyList<StyleSummaryDto>>();

        return endpointsGroup;
    }

}
