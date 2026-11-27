using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.IntegrationEvents;
using BirraPoint.Api.IntegrationTests.Modules.Organization.Import;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Persistence;

/// <summary>
/// With no foreign key between modules (FR-093) an id that points into another module is validated through that
/// module's contracts, not by the database: unknown or foreign ids keep today's responses and persist nothing
/// (R-107-6). Set up through the REST API only, so the assertions hold whichever data context serves each module.
/// </summary>
public sealed class CrossModuleReferenceTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    private static async Task<Guid> RegisterJudgeAsync(HttpClient organizer, Guid competitionId)
    {
        var email = $"ref-{Guid.NewGuid():N}@brew.example";
        var registered = await organizer.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/judges", new { emails = new[] { email } }, TestContext.Current.CancellationToken);
        Assert.True(registered.IsSuccessStatusCode, $"Register judges returned {(int)registered.StatusCode}.");

        var listed = await organizer.GetAsync(
            Urls.Rel($"/api/v1/competitions/{competitionId}/judges"), TestContext.Current.CancellationToken);
        return (await ReadAsync(listed)).EnumerateArray()
            .Single(j => j.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();
    }

    /// <summary>Imports and consolidates one beer entry into the competition; returns its id.</summary>
    private static async Task<Guid> ImportEntryAsync(HttpClient organizer, Guid competitionId)
    {
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(
            organizer, competitionId, BeerRow(email: $"brewer-{Guid.NewGuid():N}@brew.example", name: "Brewer Ref"));
        var consolidated = await ConsolidateBeerAsync(organizer, competitionId, importId);
        Assert.Equal(HttpStatusCode.OK, consolidated.StatusCode);
        return (await ReadAsync(consolidated)).GetProperty("entries")[0].GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostTableAsync(
        HttpClient organizer, Guid competitionId, IEnumerable<Guid> judgeIds, IEnumerable<Guid> beerEntryIds) =>
        organizer.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/tables",
            new { name = $"Table {Guid.NewGuid():N}", judgeIds, beerEntryIds }, TestContext.Current.CancellationToken);

    private static async Task AssertNoTablesAsync(HttpClient organizer, Guid competitionId)
    {
        var listed = await organizer.GetAsync(
            Urls.Rel($"/api/v1/competitions/{competitionId}/tables"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Empty((await ReadAsync(listed)).EnumerateArray());
    }

    [Fact]
    public async Task ConsolidatingARowWithAnUnknownStyleCodeIsRefusedAndCreatesNoEntry()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await SetStandardCategoryAsync(organizer, competitionId);
        var (importId, _) = await UploadBeerRowsAsync(organizer, competitionId, BeerRow(style: $"{UnknownStyleCode}. X"));

        var response = await ConsolidateBeerAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("urn:birrapoint:unresolved-import-rows", (await ReadAsync(response)).GetProperty("type").GetString());
        var entries = await organizer.GetAsync(
            Urls.Rel($"/api/v1/competitions/{competitionId}/entries"), TestContext.Current.CancellationToken);
        Assert.Empty((await ReadAsync(entries)).EnumerateArray());
    }

    [Fact]
    public async Task CreatingATableWithAnUnknownJudgeIdReturns400AndPersistsNothing()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var response = await PostTableAsync(organizer, competitionId, [Guid.NewGuid()], []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoTablesAsync(organizer, competitionId);
    }

    [Fact]
    public async Task CreatingATableWithAJudgeOfAnotherCompetitionReturns400AndPersistsNothing()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var otherCompetitionId = await CreateCompetitionAsync(organizer);
        var foreignJudgeId = await RegisterJudgeAsync(organizer, otherCompetitionId);

        var response = await PostTableAsync(organizer, competitionId, [foreignJudgeId], []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoTablesAsync(organizer, competitionId);
    }

    [Fact]
    public async Task CreatingATableWithAnUnknownEntryIdReturns400AndPersistsNothing()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var response = await PostTableAsync(organizer, competitionId, [], [Guid.NewGuid()]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoTablesAsync(organizer, competitionId);
    }

    [Fact]
    public async Task CreatingATableWithAnEntryOfAnotherCompetitionReturns400AndPersistsNothing()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var otherCompetitionId = await CreateCompetitionAsync(organizer);
        var foreignEntryId = await ImportEntryAsync(organizer, otherCompetitionId);

        var response = await PostTableAsync(organizer, competitionId, [], [foreignEntryId]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNoTablesAsync(organizer, competitionId);
    }

    [Fact]
    public async Task UpdatingATableWithUnknownOrForeignJudgeAndEntryIdsReturns400AndKeepsTheTable()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var otherCompetitionId = await CreateCompetitionAsync(organizer);
        var foreignJudgeId = await RegisterJudgeAsync(organizer, otherCompetitionId);
        var foreignEntryId = await ImportEntryAsync(organizer, otherCompetitionId);
        var created = await PostTableAsync(organizer, competitionId, [], []);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var table = await ReadAsync(created);
        var tableId = table.GetProperty("id").GetGuid();
        var name = table.GetProperty("name").GetString();

        foreach (var (judgeIds, entryIds) in new (Guid[], Guid[])[]
        {
            ([Guid.NewGuid()], []),
            ([foreignJudgeId], []),
            ([], [Guid.NewGuid()]),
            ([], [foreignEntryId]),
        })
        {
            var response = await organizer.PutAsJsonAsync(
                $"/api/v1/competitions/{competitionId}/tables/{tableId}",
                new { name, judgeIds, beerEntryIds = entryIds }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var listed = await organizer.GetAsync(
            Urls.Rel($"/api/v1/competitions/{competitionId}/tables"), TestContext.Current.CancellationToken);
        var tables = (await ReadAsync(listed)).EnumerateArray().ToList();
        Assert.Equal(tableId, Assert.Single(tables).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task JoiningAnUnknownTableOnTheHubIsRejected()
    {
        await using var hub = await HubTestClient.ConnectAsync(
            Factory.Server, TestJwtIssuer.IssueToken($"judge-{Guid.NewGuid():N}", $"nobody-{Guid.NewGuid():N}@brew.example", "JUDGE"));

        var error = await hub.InvokeForErrorAsync("JoinTable", Guid.NewGuid());

        Assert.NotNull(error);
        Assert.Contains("Not authorized to join this table.", error, StringComparison.Ordinal);
    }
}
