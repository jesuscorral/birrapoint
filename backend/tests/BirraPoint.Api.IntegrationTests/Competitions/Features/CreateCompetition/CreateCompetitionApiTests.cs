using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Competitions.Features.CreateCompetition;

/// <summary>POST /competitions: creation in Draft and request validation.</summary>
public sealed class CreateCompetitionApiTests(ApiFactory factory) : CompetitionsApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateReturns201WithDraftState()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/v1/competitions", ValidCreatePayload(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Draft", document.RootElement.GetProperty("state").GetString());
        Assert.NotEqual(Guid.Empty, document.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CreateReturns400WhenNameIsMissing()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateReturns400WhenEndDateIsBeforeStartDate()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = "Copa BirraPoint",
            venue = "Centro de Convenciones",
            startDate = "2026-08-03",
            endDate = "2026-08-01",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
