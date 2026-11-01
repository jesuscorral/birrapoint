using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Competitions;

/// <summary>Shared organizer client and competition seeding for the Competitions slice contract
/// tests (contracts/rest-api.md §Competitions).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "Base class of public xUnit test classes; C# requires a base class to be at least as accessible as its derived classes.")]
public abstract class CompetitionsApiTestBase(ApiFactory factory)
{
    private protected ApiFactory Factory => factory;

    private protected HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private protected static object ValidCreatePayload(string namePrefix = "Copa") => new
    {
        name = $"{namePrefix} {Guid.NewGuid():N}",
        venue = "Centro de Convenciones",
        startDate = "2026-08-01",
        endDate = "2026-08-03",
    };

    private protected static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Copa")
    {
        var response = await client.PostAsJsonAsync("/api/v1/competitions", ValidCreatePayload(namePrefix));
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }
}
