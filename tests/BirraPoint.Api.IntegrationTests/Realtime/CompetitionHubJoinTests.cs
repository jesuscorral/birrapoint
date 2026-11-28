using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.IntegrationEvents;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Realtime;

/// <summary>
/// Group-join authorization of <c>CompetitionHub</c> (contracts/signalr-hub.md): an organizer joins only the
/// competitions they own; a judge joins only a table where they are an active member, identified by Keycloak id
/// or, until that id is backfilled, by email. The setup goes through the REST API only, so the scenarios hold
/// whichever data context the hub's lookups end up behind.
/// </summary>
public sealed class CompetitionHubJoinTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string NotAuthorizedOrganizer = "Not authorized to join this competition's organizer group.";
    private const string NotAuthorizedTable = "Not authorized to join this table.";

    private sealed record Setup(
        string OrganizerSub, Guid CompetitionId, Guid TableId, string SeatedEmail, string OtherSeatedEmail, string UnseatedEmail,
        IReadOnlyDictionary<string, Guid> JudgeIds);

    private HttpClient Client(string sub, string? email, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, email, role));
        return client;
    }

    /// <summary>A competition with two judges seated at one table and a third judge registered but not seated.</summary>
    private async Task<Setup> SeedAsync()
    {
        var organizerSub = $"organizer-{Guid.NewGuid():N}";
        using var organizer = Client(organizerSub, null, "ORGANIZER");
        var created = await organizer.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = $"Hub {Guid.NewGuid():N}",
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var competitionId = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("id").GetGuid();

        string[] emails =
        [
            $"seated-{Guid.NewGuid():N}@brew.example",
            $"other-{Guid.NewGuid():N}@brew.example",
            $"unseated-{Guid.NewGuid():N}@brew.example",
        ];
        var registered = await organizer.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/judges", new { emails }, TestContext.Current.CancellationToken);
        Assert.True(registered.IsSuccessStatusCode, $"Register judges returned {(int)registered.StatusCode}.");

        var listed = await organizer.GetAsync(
            Urls.Rel($"/api/v1/competitions/{competitionId}/judges"), TestContext.Current.CancellationToken);
        var judgeIds = (await listed.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .EnumerateArray().ToDictionary(j => j.GetProperty("email").GetString()!, j => j.GetProperty("id").GetGuid(), StringComparer.Ordinal);

        var table = await organizer.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/tables",
            new { name = $"Table {Guid.NewGuid():N}", judgeIds = new[] { judgeIds[emails[0]], judgeIds[emails[1]] }, beerEntryIds = Array.Empty<Guid>() },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, table.StatusCode);
        var tableId = (await table.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("id").GetGuid();

        return new Setup(organizerSub, competitionId, tableId, emails[0], emails[1], emails[2], judgeIds);
    }

    private async Task<string?> JoinTableAsync(string sub, string? email, Guid tableId)
    {
        await using var hub = await HubTestClient.ConnectAsync(factory.Server, TestJwtIssuer.IssueToken(sub, email, "JUDGE"));
        return await hub.InvokeForErrorAsync("JoinTable", tableId);
    }

    private async Task<string?> JoinCompetitionAsync(string sub, Guid competitionId)
    {
        await using var hub = await HubTestClient.ConnectAsync(factory.Server, TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return await hub.InvokeForErrorAsync("JoinCompetitionAsOrganizer", competitionId);
    }

    /// <summary>Any authenticated judge call resolves the judge row and backfills its Keycloak id with the token's <c>sub</c>.</summary>
    private async Task BackfillKeycloakIdAsync(string sub, string email)
    {
        using var judge = Client(sub, email, "JUDGE");
        var response = await judge.GetAsync(Urls.Rel("/api/v1/me/tables"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnOrganizerJoinsTheOrganizerGroupOfACompetitionTheyOwn()
    {
        var setup = await SeedAsync();

        Assert.Null(await JoinCompetitionAsync(setup.OrganizerSub, setup.CompetitionId));
    }

    [Fact]
    public async Task AnOrganizerIsRejectedOnACompetitionOwnedBySomeoneElse()
    {
        var setup = await SeedAsync();

        var error = await JoinCompetitionAsync($"organizer-{Guid.NewGuid():N}", setup.CompetitionId);

        Assert.NotNull(error);
        Assert.Contains(NotAuthorizedOrganizer, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrganizerIsRejectedOnAnUnknownCompetition()
    {
        var setup = await SeedAsync();

        var error = await JoinCompetitionAsync(setup.OrganizerSub, Guid.NewGuid());

        Assert.NotNull(error);
        Assert.Contains(NotAuthorizedOrganizer, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJudgeSeatedAtTheTableJoinsByEmailBeforeTheirKeycloakIdIsBackfilled()
    {
        var setup = await SeedAsync();

        Assert.Null(await JoinTableAsync($"judge-{Guid.NewGuid():N}", setup.SeatedEmail, setup.TableId));
    }

    [Fact]
    public async Task AJudgeSeatedAtTheTableJoinsByKeycloakIdOnceItIsBackfilled()
    {
        var setup = await SeedAsync();
        var sub = $"judge-{Guid.NewGuid():N}";
        await BackfillKeycloakIdAsync(sub, setup.SeatedEmail);

        // No email claim at all: membership rests on the Keycloak id alone.
        Assert.Null(await JoinTableAsync(sub, email: null, setup.TableId));
    }

    [Fact]
    public async Task ABackfilledJudgeCannotBeImpersonatedThroughTheEmailFallback()
    {
        var setup = await SeedAsync();
        await BackfillKeycloakIdAsync($"judge-{Guid.NewGuid():N}", setup.SeatedEmail);

        var error = await JoinTableAsync($"judge-{Guid.NewGuid():N}", setup.SeatedEmail, setup.TableId);

        Assert.NotNull(error);
        Assert.Contains(NotAuthorizedTable, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARegisteredJudgeWhoIsNotSeatedAtTheTableIsRejected()
    {
        var setup = await SeedAsync();

        var error = await JoinTableAsync($"judge-{Guid.NewGuid():N}", setup.UnseatedEmail, setup.TableId);

        Assert.NotNull(error);
        Assert.Contains(NotAuthorizedTable, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStrangerWithNoJudgeRowIsRejected()
    {
        var setup = await SeedAsync();

        var error = await JoinTableAsync($"judge-{Guid.NewGuid():N}", $"stranger-{Guid.NewGuid():N}@brew.example", setup.TableId);

        Assert.NotNull(error);
        Assert.Contains(NotAuthorizedTable, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJudgeRemovedFromTheTableIsRejected()
    {
        var setup = await SeedAsync();
        Assert.Null(await JoinTableAsync($"judge-{Guid.NewGuid():N}", setup.OtherSeatedEmail, setup.TableId));

        using var organizer = Client(setup.OrganizerSub, null, "ORGANIZER");
        var update = await organizer.PutAsJsonAsync(
            $"/api/v1/competitions/{setup.CompetitionId}/tables/{setup.TableId}",
            new
            {
                name = $"Table {Guid.NewGuid():N}",
                judgeIds = new[] { setup.JudgeIds[setup.SeatedEmail] },
                beerEntryIds = Array.Empty<Guid>(),
            }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var error = await JoinTableAsync($"judge-{Guid.NewGuid():N}", setup.OtherSeatedEmail, setup.TableId);

        Assert.NotNull(error);
        Assert.Contains(NotAuthorizedTable, error, StringComparison.Ordinal);
        Assert.Null(await JoinTableAsync($"judge-{Guid.NewGuid():N}", setup.SeatedEmail, setup.TableId));
    }

    [Fact]
    public async Task JoiningAnUnknownTableIsRejected()
    {
        var setup = await SeedAsync();

        var error = await JoinTableAsync($"judge-{Guid.NewGuid():N}", setup.SeatedEmail, Guid.NewGuid());

        Assert.NotNull(error);
        Assert.Contains(NotAuthorizedTable, error, StringComparison.Ordinal);
    }
}
