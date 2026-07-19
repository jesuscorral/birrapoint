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
    public async Task Create_returns_201_with_draft_state()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/v1/competitions", ValidCreatePayload());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Draft", document.RootElement.GetProperty("state").GetString());
        Assert.NotEqual(Guid.Empty, document.RootElement.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Create_returns_400_when_name_is_missing()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_returns_400_when_end_date_is_before_start_date()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = "Copa BirraPoint",
            venue = "Centro de Convenciones",
            startDate = "2026-08-03",
            endDate = "2026-08-01",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_returns_only_the_callers_own_competitions()
    {
        using var ownerClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        using var otherClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        await CreateCompetitionAsync(ownerClient, "Mine");
        await CreateCompetitionAsync(otherClient, "TheirsOnly");

        var response = await ownerClient.GetAsync("/api/v1/competitions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.EnumerateArray().ToList();

        Assert.Single(items);
    }

    [Fact]
    public async Task Get_by_id_returns_404_for_a_competition_owned_by_a_different_organizer()
    {
        using var ownerClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        using var otherClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(ownerClient, "Cross");

        var response = await otherClient.GetAsync($"/api/v1/competitions/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_by_id_returns_404_when_the_competition_does_not_exist()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var response = await client.GetAsync($"/api/v1/competitions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_by_id_returns_the_full_detail_for_the_owner()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Detail");

        var response = await client.GetAsync($"/api/v1/competitions/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(id, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Draft", document.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Update_replaces_editable_fields_while_in_draft()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Update");

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{id}", new
        {
            name = "Renamed Competition",
            venue = "New Venue",
            startDate = "2026-09-01",
            endDate = "2026-09-05",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Renamed Competition", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("New Venue", document.RootElement.GetProperty("venue").GetString());
    }

    [Fact]
    public async Task Update_returns_404_for_a_competition_owned_by_a_different_organizer()
    {
        using var ownerClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        using var otherClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(ownerClient, "CrossUpdate");

        var response = await otherClient.PutAsJsonAsync($"/api/v1/competitions/{id}", ValidCreatePayload("ShouldNotApply"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_returns_409_when_the_competition_is_in_evaluation()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Locked");
        await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Active" });
        await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "InEvaluation" });

        var response = await client.PutAsJsonAsync($"/api/v1/competitions/{id}", ValidCreatePayload("ShouldFail"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task State_transition_moves_forward_through_the_full_lifecycle()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Lifecycle");

        var toActive = await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Active" });
        Assert.Equal(HttpStatusCode.OK, toActive.StatusCode);

        var toInEvaluation = await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "InEvaluation" });
        Assert.Equal(HttpStatusCode.OK, toInEvaluation.StatusCode);

        var toFinalized = await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Finalized" });
        Assert.Equal(HttpStatusCode.OK, toFinalized.StatusCode);

        using var document = JsonDocument.Parse(await toFinalized.Content.ReadAsStringAsync());
        Assert.Equal("Finalized", document.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task State_transition_rejects_skipping_a_state()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Skip");

        var response = await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "InEvaluation" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task State_transition_rejects_moving_backward()
    {
        using var client = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(client, "Backward");
        await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Active" });

        var response = await client.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Draft" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task State_transition_returns_404_for_a_competition_owned_by_a_different_organizer()
    {
        using var ownerClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        using var otherClient = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        var id = await CreateCompetitionAsync(ownerClient, "CrossState");

        var response = await otherClient.PostAsJsonAsync($"/api/v1/competitions/{id}/state", new { target = "Active" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
