using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.RateLimiting;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Judges;

/// <summary>
/// HTTP-level tests for the NotifyJudges rate limit (fixed window, partitioned per organizer `sub`).
/// Each test uses fresh organizer subs so the shared host's limiter state never leaks between tests.
/// </summary>
public sealed class NotifyJudgesRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = $"RateLimit {Guid.NewGuid():N}",
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return created.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> NotifyJudgesAsync(HttpClient client, Guid competitionId) =>
        client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/judges/notify"), null, TestContext.Current.CancellationToken);

    private static async Task ExhaustNotifyBudgetAsync(HttpClient client, Guid competitionId)
    {
        for (var i = 0; i < RateLimitingExtensions.NotifyJudgesPermitLimit; i++)
        {
            using var response = await NotifyJudgesAsync(client, competitionId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task NotifyBeyondThePermitLimitWithinTheWindowIsRejectedWith429()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        await ExhaustNotifyBudgetAsync(organizer, competitionId);
        using var rejected = await NotifyJudgesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    [Fact]
    public async Task AnOrganizerHittingTheLimitDoesNotThrottleAnotherOrganizer()
    {
        using var first = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var firstCompetition = await CreateCompetitionAsync(first);
        await ExhaustNotifyBudgetAsync(first, firstCompetition);
        using (var rejected = await NotifyJudgesAsync(first, firstCompetition))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        }

        using var second = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var secondCompetition = await CreateCompetitionAsync(second);
        using var response = await NotifyJudgesAsync(second, secondCompetition);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OtherJudgesEndpointsAreNotRateLimitedAfterNotifyIsThrottled()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await ExhaustNotifyBudgetAsync(organizer, competitionId);

        for (var i = 0; i < 5; i++)
        {
            using var response = await organizer.GetAsync(
                Urls.Rel($"/api/v1/competitions/{competitionId}/judges"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task UnauthenticatedNotifyCallsAreRejectedWith401AndDoNotConsumeTheBudget()
    {
        using var anonymous = factory.CreateClient();
        for (var i = 0; i < RateLimitingExtensions.NotifyJudgesPermitLimit + 1; i++)
        {
            using var response = await NotifyJudgesAsync(anonymous, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        await ExhaustNotifyBudgetAsync(organizer, competitionId);
    }
}
