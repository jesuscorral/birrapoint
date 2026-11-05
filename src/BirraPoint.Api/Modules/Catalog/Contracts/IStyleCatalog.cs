namespace BirraPoint.Api.Modules.Catalog.Contracts;

/// <summary>Public read surface of the Catalog module: the only way other modules look up BJCP styles.</summary>
internal interface IStyleCatalog
{
    /// <summary>Every style in the catalog (entry import matching by code or name).</summary>
    Task<IReadOnlyList<StyleSummaryDto>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Styles for the given codes, keyed by code (ordinal). Unknown codes are simply absent, so
    /// callers can check code existence and resolve names with one call.</summary>
    Task<IReadOnlyDictionary<string, StyleSummaryDto>> GetByCodesAsync(
        IReadOnlyCollection<string> codes, CancellationToken cancellationToken = default);
}
