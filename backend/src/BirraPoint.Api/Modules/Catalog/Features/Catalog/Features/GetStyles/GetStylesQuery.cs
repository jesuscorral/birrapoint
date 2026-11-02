using System.Globalization;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Catalog.Features.Catalog.Features.GetStyles;


internal sealed record GetStylesQuery : IRequest<IReadOnlyList<StyleSummaryDto>>;

internal sealed class GetStylesQueryHandler(AppDbContext dbContext)
    : IRequestHandler<GetStylesQuery, IReadOnlyList<StyleSummaryDto>>
{
    public async Task<IReadOnlyList<StyleSummaryDto>> Handle(GetStylesQuery request, CancellationToken cancellationToken)
    {
        var styles = await dbContext.BjcpStyles
            .Select(style => new StyleSummaryDto(style.Code, style.Name, style.CategoryNumber, style.CategoryName))
            .ToListAsync(cancellationToken);

        return styles
            .OrderBy(style => int.TryParse(style.CategoryNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue)
            .ThenBy(style => style.CategoryNumber, StringComparer.Ordinal)
            .ThenBy(style => style.Code, StringComparer.Ordinal)
            .ToList();
    }
}
