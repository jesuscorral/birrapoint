using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Competitions.Features.UpdateCompetition;

/// <summary>PUT /competitions/{id}: full replace of editable fields, Draft/Active edit gate.</summary>
public sealed class UpdateCompetitionApiTests(ApiFactory factory) : CompetitionsApiTestBase(factory), IClassFixture<ApiFactory>
{
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
}
