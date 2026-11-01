using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Competitions.Features.ListCompetitions;

/// <summary>GET /competitions: owner-scoped listing.</summary>
public sealed class ListCompetitionsApiTests(ApiFactory factory) : CompetitionsApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ListReturnsOnlyTheCallersOwnCompetitions()
    {
        using var ownerClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        using var otherClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        await CreateCompetitionAsync(ownerClient, "Mine");
        await CreateCompetitionAsync(otherClient, "TheirsOnly");

        var response = await ownerClient.GetAsync(Urls.Rel("/api/v1/competitions"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var items = document.RootElement.EnumerateArray().ToList();

        Assert.Single(items);
    }
}
