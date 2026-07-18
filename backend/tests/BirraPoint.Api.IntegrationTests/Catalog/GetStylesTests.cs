using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Catalog;

/// <summary>
/// T018: first HTTP-level contract test for the pipeline (GET /api/v1/styles, T017) — proves the
/// harness enforces auth end to end and that the wire shape matches contracts/rest-api.md §Catalog.
/// </summary>
public sealed class GetStylesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Request_without_a_bearer_token_is_rejected()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/styles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_caller_of_any_role_receives_the_full_catalog_in_category_order()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: "kc-user-1"));

        var response = await client.GetAsync("/api/v1/styles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var styles = document.RootElement.EnumerateArray().ToList();

        Assert.Equal(FullCatalogSize, styles.Count);
        Assert.All(styles, style => Assert.Equal(
            new HashSet<string> { "code", "name", "categoryNumber", "categoryName" },
            style.EnumerateObject().Select(property => property.Name).ToHashSet()));

        // Distinct categories, in wire order, must be numerically ascending (Appendix B's
        // non-numeric "X" category last) — the T017 review fix over a plain lexicographic
        // OrderBy, which would place "10" before "2".
        var categoriesInOrder = styles
            .Select(style => style.GetProperty("categoryNumber").GetString()!)
            .Distinct()
            .ToList();
        var numericallySorted = categoriesInOrder
            .OrderBy(category => int.TryParse(category, out var number) ? number : int.MaxValue)
            .ThenBy(category => category, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(numericallySorted, categoriesInOrder);
        Assert.Equal("1", categoriesInOrder[0]);
    }

    private const int FullCatalogSize = 125; // BJCP 2021: categories 1-34 + Appendix B (T010)
}
