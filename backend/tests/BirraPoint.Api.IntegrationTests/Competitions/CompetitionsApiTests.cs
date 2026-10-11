using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Competitions;

/// <summary>
/// T026: HTTP-level contract tests for the Competitions slices (contracts/rest-api.md
/// §Competitions) — creation, owner-scoped listing/detail (404 never reveals cross-owner
/// existence), edit gating, and the FR-006 forward-only/skip-free state machine.
/// </summary>
public sealed class CompetitionsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private static object ValidCreatePayload(string namePrefix = "Copa") => new
    {
        name = $"{namePrefix} {Guid.NewGuid():N}",
        venue = "Centro de Convenciones",
        startDate = "2026-08-01",
        endDate = "2026-08-03",
    };

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Copa")
    {
        var response = await client.PostAsJsonAsync("/api/v1/competitions", ValidCreatePayload(namePrefix));
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

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

    [Fact]
    public async Task UpdateReplacesEditableFieldsWhileInDraft()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Update");

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{id}", new
        {
            name = "Renamed Competition",
            venue = "New Venue",
            startDate = "2026-09-01",
            endDate = "2026-09-05",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Renamed Competition", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("New Venue", document.RootElement.GetProperty("venue").GetString());
    }

    [Fact]
    public async Task UpdateReturns404ForACompetitionOwnedByADifferentOrganizer()
    {
        using var ownerClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        using var otherClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(ownerClient, "CrossUpdate");

        var response = await otherClient.PutAsJsonAsync($"/api/v1/competitions/{id}", ValidCreatePayload("ShouldNotApply"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateReturns409WhenTheCompetitionIsInEvaluation()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Locked");
        await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Active" }, cancellationToken: TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "InEvaluation" }, cancellationToken: TestContext.Current.CancellationToken);

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{id}", ValidCreatePayload("ShouldFail"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

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
