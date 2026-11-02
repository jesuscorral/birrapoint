using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Catalog.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Catalog.Integration;

/// <summary>IStyleCatalog: the Catalog module's public read surface over the BJCP 2021 seed (FR-049).</summary>
public sealed class StyleCatalogTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static IStyleCatalog Catalog(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IStyleCatalog>();

    [Fact]
    public async Task ListAsyncReturnsTheSeededCatalogWithSummaryFields()
    {
        using var scope = factory.Services.CreateScope();

        var styles = await Catalog(scope).ListAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(styles);
        var apa = Assert.Single(styles, s => s.Code == "21A");
        Assert.False(string.IsNullOrWhiteSpace(apa.Name));
        Assert.Equal("21", apa.CategoryNumber);
        Assert.False(string.IsNullOrWhiteSpace(apa.CategoryName));
        Assert.NotNull(apa.AbvLow);
        Assert.NotNull(apa.AbvHigh);
    }

    [Fact]
    public async Task GetByCodesAsyncReturnsKnownCodesKeyedByCodeAndOmitsUnknownOnes()
    {
        using var scope = factory.Services.CreateScope();

        var result = await Catalog(scope).GetByCodesAsync(["21A", "NOPE-99"], TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal("21A", result["21A"].Code);
        Assert.False(result.ContainsKey("NOPE-99"));
    }

    [Fact]
    public async Task GetByCodesAsyncWithNoCodesReturnsAnEmptyDictionary()
    {
        using var scope = factory.Services.CreateScope();

        var result = await Catalog(scope).GetByCodesAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByCodesAsyncMatchesCodesOrdinally()
    {
        using var scope = factory.Services.CreateScope();

        var result = await Catalog(scope).GetByCodesAsync(["21a"], TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }
}
