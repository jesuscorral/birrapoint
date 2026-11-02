using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Competitions.Features.SetCompetitionCategories;

/// <summary>PUT/GET /competitions/{id}/categories (contracts/rest-api.md §Competitions): organizer-defined groupings of allowed BJCP styles (wizard step 3) — owner-scoped PUT/GET roundtrip, full-replace semantics, validation and state gating. Needs real BjcpStyle rows, so the DB-backed rules live here; SetCompetitionCategoriesCommandValidatorTests covers only the sync rules.</summary>
public sealed class SetCompetitionCategoriesApiTests(ApiFactory factory) : CompetitionsApiTestBase(factory), IClassFixture<ApiFactory>
{
    private const string StyleCodeApa = "21A";
    private static readonly string[] UnknownStyleCodes = ["99Z"];
    private const string StyleCodeBlonde = "1A";

    [Fact]
    public async Task PutThenGetRoundtripReturnsTheSameCategoriesAndStyles()
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
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var getResponse = await client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/categories"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var document = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var categories = document.RootElement.GetProperty("categories").EnumerateArray().ToList();

        Assert.Equal(2, categories.Count);
        Assert.Equal("Classic Styles", categories[0].GetProperty("name").GetString());
        Assert.Equal(StyleCodeApa, categories[0].GetProperty("styleCodes").EnumerateArray().Single().GetString());
        Assert.Equal("Pale Styles", categories[1].GetProperty("name").GetString());
        Assert.Equal(StyleCodeBlonde, categories[1].GetProperty("styleCodes").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task PutReturns404ForACompetitionOwnedByADifferentOrganizer()
    {
        using var ownerClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        using var otherClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var competitionId = await CreateCompetitionAsync(ownerClient, "Cross");

        var response = await otherClient.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Classic Styles", displayOrder = 0, styleCodes = new[] { StyleCodeApa } } },
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutReturns409WhenTheCompetitionIsFinalized()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(client, "Locked");

        await client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Active" }, cancellationToken: TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "InEvaluation" }, cancellationToken: TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Finalized" }, cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Classic Styles", displayOrder = 0, styleCodes = new[] { StyleCodeApa } } },
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task PutReturns400WhenTheSameStyleCodeAppearsInTwoCategories()
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
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutReturns400ForAnUnknownStyleCode()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(client, "UnknownCode");

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Classic Styles", displayOrder = 0, styleCodes = UnknownStyleCodes } },
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RePutFullyReplacesThePreviousCategorySet()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(client, "Replace");

        await client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Classic Styles", displayOrder = 0, styleCodes = new[] { StyleCodeApa } } },
        }, cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/categories", new
        {
            categories = new[] { new { name = "Pale Styles", displayOrder = 0, styleCodes = new[] { StyleCodeBlonde } } },
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var getResponse = await client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/categories"), TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var categories = document.RootElement.GetProperty("categories").EnumerateArray().ToList();

        Assert.Single(categories);
        Assert.Equal("Pale Styles", categories[0].GetProperty("name").GetString());
        Assert.Equal(StyleCodeBlonde, categories[0].GetProperty("styleCodes").EnumerateArray().Single().GetString());
    }
}
