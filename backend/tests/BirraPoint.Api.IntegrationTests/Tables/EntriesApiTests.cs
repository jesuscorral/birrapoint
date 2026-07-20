using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Tables;

/// <summary>
/// Contract tests for `GET /api/v1/competitions/{id}/entries` — feeds T048's "Unassigned" source
/// column (every entry not currently on a table). ORGANIZER-only; `tastingTableId`/
/// `tastingTableName` are null until an entry is assigned to a table via CreateTable/UpdateTable.
/// </summary>
public sealed class EntriesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string StyleCodeApa = "21A";

    private HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "JUDGE"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = $"Entries {Guid.NewGuid():N}",
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> GetEntriesAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync($"/api/v1/competitions/{competitionId}/entries");

    private static Task<HttpResponseMessage> CreateTableAsync(
        HttpClient client, Guid competitionId, string name, IEnumerable<Guid> judgeIds, IEnumerable<Guid> beerEntryIds) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/tables", new { name, judgeIds, beerEntryIds });

    private static string NewBlindCode() => $"B{Guid.NewGuid():N}"[..8];

    private async Task<Guid> SeedParticipantAsync(Guid competitionId, string name, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var participant = new Participant { CompetitionId = competitionId, Name = name, Email = email };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();
        return participant.Id;
    }

    private async Task<Guid> SeedBeerEntryAsync(Guid competitionId, Guid participantId, string beerName, string styleCode = StyleCodeApa)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = new BeerEntry
        {
            CompetitionId = competitionId,
            ParticipantId = participantId,
            BeerName = beerName,
            StyleCode = styleCode,
            BlindCode = NewBlindCode(),
        };
        db.BeerEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    [Fact]
    public async Task Get_without_a_bearer_token_is_rejected_with_401()
    {
        using var client = factory.CreateClient();

        var response = await GetEntriesAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetEntriesAsync(judge, competitionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetEntriesAsync(other, competitionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_null_table_fields_before_assignment_and_the_table_after_assignment()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Hop Cannon");

        var beforeResponse = await GetEntriesAsync(organizer, competitionId);
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        using var before = JsonDocument.Parse(await beforeResponse.Content.ReadAsStringAsync());
        var beforeEntry = before.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == entryId);

        Assert.Equal("Hop Cannon", beforeEntry.GetProperty("beerName").GetString());
        Assert.Equal(StyleCodeApa, beforeEntry.GetProperty("styleCode").GetString());
        Assert.False(string.IsNullOrEmpty(beforeEntry.GetProperty("styleName").GetString()));
        Assert.False(beforeEntry.GetProperty("notValidForBos").GetBoolean());
        Assert.Equal(JsonValueKind.Null, beforeEntry.GetProperty("tastingTableId").ValueKind);
        Assert.Equal(JsonValueKind.Null, beforeEntry.GetProperty("tastingTableName").ValueKind);

        var tableName = $"Table {Guid.NewGuid():N}";
        var createResponse = await CreateTableAsync(organizer, competitionId, tableName, [], [entryId]);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var tableId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var afterResponse = await GetEntriesAsync(organizer, competitionId);
        using var after = JsonDocument.Parse(await afterResponse.Content.ReadAsStringAsync());
        var afterEntry = after.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == entryId);

        Assert.Equal(tableId, afterEntry.GetProperty("tastingTableId").GetGuid());
        Assert.Equal(tableName, afterEntry.GetProperty("tastingTableName").GetString());
    }
}
