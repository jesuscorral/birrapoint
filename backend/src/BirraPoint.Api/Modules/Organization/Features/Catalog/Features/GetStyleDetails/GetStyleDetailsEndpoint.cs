using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Catalog.Features.GetStyleDetails;

internal static class GetStyleDetailsEndpoint
{
    internal static IEndpointRouteBuilder MapGetStyleDetailsEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/{code}", async (string code, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetStyleDetailQuery(code), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
            .WithName("GetStyleDetail")
            .Produces<StyleDetailDto>()
            .Produces(StatusCodes.Status404NotFound);
        return endpointsGroup;
    }

}
