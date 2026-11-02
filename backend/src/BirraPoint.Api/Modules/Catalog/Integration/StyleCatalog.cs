using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Catalog.Integration;

internal sealed class StyleCatalog(AppDbContext dbContext) : IStyleCatalog
{
    public async Task<IReadOnlyList<StyleSummaryDto>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.BjcpStyles
            .AsNoTracking()
            .Select(s => new StyleSummaryDto(s.Code, s.Name, s.CategoryNumber, s.CategoryName, s.ABVLow, s.ABVHigh))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<string, StyleSummaryDto>> GetByCodesAsync(
        IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default)
    {
        if (codes.Count == 0)
        {
            return new Dictionary<string, StyleSummaryDto>(StringComparer.Ordinal);
        }

        var codeList = codes.Distinct(StringComparer.Ordinal).ToList();
        var styles = await dbContext.BjcpStyles
            .AsNoTracking()
            .Where(s => codeList.Contains(s.Code))
            .Select(s => new StyleSummaryDto(s.Code, s.Name, s.CategoryNumber, s.CategoryName, s.ABVLow, s.ABVHigh))
            .ToListAsync(cancellationToken);

        return styles.ToDictionary(s => s.Code, StringComparer.Ordinal);
    }
}
