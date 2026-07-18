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

        Assert.Equal(125, styles.Count);
        Assert.All(styles, style => Assert.Equal(
            new HashSet<string> { "code", "name", "categoryNumber", "categoryName" },
            style.EnumerateObject().Select(property => property.Name).ToHashSet()));

        // Category order is numeric (T017 review fix), not lexicographic: "1" then "2", never "10"
        // before "2". The catalog's first category is "1" (Standard American Beer).
        Assert.Equal("1", styles[0].GetProperty("categoryNumber").GetString());
    }
}
