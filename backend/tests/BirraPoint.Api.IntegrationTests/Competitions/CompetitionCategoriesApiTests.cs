using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Competitions;

/// <summary>
/// HTTP-level contract tests for the Competitions/{id}/categories slice (contracts/rest-api.md
/// §Competitions) — organizer-defined groupings of allowed BJCP styles (wizard step 3):
/// owner-scoped PUT/GET roundtrip, full-replace semantics, and validation/state gating. The
/// style-existence and happy-path checks that need real BjcpStyle catalog rows live here rather
/// than in the validator's unit tests (SetCompetitionCategoriesCommandValidatorTests covers only
/// the sync rules) — same split already used for CreateTableCommandValidator/
/// ResolveRowCommandValidator's own DB-backed rules in this codebase.
/// </summary>
public sealed class CompetitionCategoriesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string StyleCodeApa = "21A";
    private const string StyleCodeBlonde = "1A";

    private HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Copa")
    {
        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = $"{namePrefix} {Guid.NewGuid():N}",
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Put_then_get_roundtrip_returns_the_same_categories_and_styles()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(client, "Roundtrip");

        var putResponse = await client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[]
            {
                new { name = "Classic Styles", displayOrder = 0, styleCodes = new[] { StyleCodeApa } },
                new { name = "Pale Styles", displayOrder = 1, styleCodes = new[] { StyleCodeBlonde } },
            },
        });

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/competitions/{competitionId}/categories");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var document = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        var categories = document.RootElement.GetProperty("categories").EnumerateArray().ToList();

        Assert.Equal(2, categories.Count);
        Assert.Equal("Classic Styles", categories[0].GetProperty("name").GetString());
        Assert.Equal(StyleCodeApa, categories[0].GetProperty("styleCodes").EnumerateArray().Single().GetString());
        Assert.Equal("Pale Styles", categories[1].GetProperty("name").GetString());
        Assert.Equal(StyleCodeBlonde, categories[1].GetProperty("styleCodes").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task Put_returns_404_for_a_competition_owned_by_a_different_organizer()
    {
        using var ownerClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        using var otherClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var competitionId = await CreateCompetitionAsync(ownerClient, "Cross");

        var response = await otherClient.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Classic Styles", displayOrder = 0, styleCodes = new[] { StyleCodeApa } } },
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_409_when_the_competition_is_finalized()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(client, "Locked");

        await client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Active" });
        await client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "InEvaluation" });
        await client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Finalized" });

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Classic Styles", displayOrder = 0, styleCodes = new[] { StyleCodeApa } } },
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Put_returns_400_when_the_same_style_code_appears_in_two_categories()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(client, "DuplicateCode");

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[]
            {
                new { name = "Classic Styles", displayOrder = 0, styleCodes = new[] { StyleCodeApa } },
                new { name = "Modern Styles", displayOrder = 1, styleCodes = new[] { StyleCodeApa } },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_400_for_an_unknown_style_code()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(client, "UnknownCode");

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Classic Styles", displayOrder = 0, styleCodes = new[] { "99Z" } } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Re_put_fully_replaces_the_previous_category_set()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(client, "Replace");

        await client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Classic Styles", displayOrder = 0, styleCodes = new[] { StyleCodeApa } } },
        });

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Pale Styles", displayOrder = 0, styleCodes = new[] { StyleCodeBlonde } } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/competitions/{competitionId}/categories");
        using var document = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        var categories = document.RootElement.GetProperty("categories").EnumerateArray().ToList();

        Assert.Single(categories);
        Assert.Equal("Pale Styles", categories[0].GetProperty("name").GetString());
        Assert.Equal(StyleCodeBlonde, categories[0].GetProperty("styleCodes").EnumerateArray().Single().GetString());
    }
}
