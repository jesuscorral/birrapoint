using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Competitions.Features.GetCompetition;

/// <summary>GET /competitions/{id}: owner-scoped detail; 404 never reveals cross-owner existence.</summary>
public sealed class GetCompetitionApiTests(ApiFactory factory) : CompetitionsApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetByIdReturns404ForACompetitionOwnedByADifferentOrganizer()
    {
        using var ownerClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        using var otherClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(ownerClient, "Cross");

        var response = await otherClient.GetAsync(Urls.Rel($"/api/v1/competitions/{id}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetByIdReturns404WhenTheCompetitionDoesNotExist()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var response = await client.GetAsync(Urls.Rel($"/api/v1/competitions/{Guid.NewGuid()}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetByIdReturnsTheFullDetailForTheOwner()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Detail");

        var response = await client.GetAsync(Urls.Rel($"/api/v1/competitions/{id}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(id, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Draft", document.RootElement.GetProperty("state").GetString());
    }
}
