using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Catalog;

/// <summary>
/// T057B: HTTP-level contract test for GET /api/v1/styles/{code} (contracts/rest-api.md §Catalog,
/// FR-049) — the full vital-statistics + guide-description shape for a real seeded BJCP 2021
/// style, and the 404 for an unknown code.
/// </summary>
public sealed class GetStyleDetailTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string KnownStyleCode = "21A"; // American IPA (seeded, T010)

    private HttpClient AuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: $"kc-user-{Guid.NewGuid():N}"));
        return client;
    }

    [Fact]
    public async Task Known_style_code_returns_200_with_the_full_contracted_shape()
    {
        using var client = AuthenticatedClient();

        var response = await client.GetAsync($"/api/v1/styles/{KnownStyleCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(KnownStyleCode, root.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("categoryNumber").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("categoryName").GetString()));

        var vitalStatistics = root.GetProperty("vitalStatistics");
        Assert.Equal(
            new HashSet<string> { "ogLow", "ogHigh", "fgLow", "fgHigh", "ibuLow", "ibuHigh", "srmLow", "srmHigh", "abvLow", "abvHigh" },
            vitalStatistics.EnumerateObject().Select(p => p.Name).ToHashSet());

        var description = root.GetProperty("description");
        Assert.Equal(
            new HashSet<string>
            {
                "overallImpression", "aroma", "appearance", "flavor", "mouthfeel", "comments", "history",
                "characteristicIngredients", "styleComparison", "entryInstructions", "commercialExamples", "tags",
            },
            description.EnumerateObject().Select(p => p.Name).ToHashSet());

        Assert.False(string.IsNullOrEmpty(description.GetProperty("overallImpression").GetString()));
        Assert.Equal(JsonValueKind.Array, description.GetProperty("commercialExamples").ValueKind);
        Assert.Equal(JsonValueKind.Array, description.GetProperty("tags").ValueKind);
    }

    [Fact]
    public async Task Unknown_style_code_returns_404()
    {
        using var client = AuthenticatedClient();

        var response = await client.GetAsync("/api/v1/styles/NOT-A-REAL-CODE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
