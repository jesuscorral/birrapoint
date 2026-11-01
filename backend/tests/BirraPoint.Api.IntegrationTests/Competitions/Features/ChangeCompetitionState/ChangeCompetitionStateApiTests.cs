using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Competitions.Features.ChangeCompetitionState;

/// <summary>POST /competitions/{id}/state: FR-006 forward-only, skip-free state machine.</summary>
public sealed class ChangeCompetitionStateApiTests(ApiFactory factory) : CompetitionsApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task StateTransitionMovesForwardThroughTheFullLifecycle()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Lifecycle");

        var toActive = await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Active" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, toActive.StatusCode);

        var toInEvaluation = await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "InEvaluation" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, toInEvaluation.StatusCode);

        var toFinalized = await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Finalized" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, toFinalized.StatusCode);

        using var document = JsonDocument.Parse(await toFinalized.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Finalized", document.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task StateTransitionRejectsSkippingAState()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Skip");

        var response = await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "InEvaluation" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task StateTransitionRejectsMovingBackward()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Backward");
        await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Active" }, cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Draft" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task StateTransitionReturns404ForACompetitionOwnedByADifferentOrganizer()
    {
        using var ownerClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        using var otherClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(ownerClient, "CrossState");

        var response = await otherClient.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Active" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
