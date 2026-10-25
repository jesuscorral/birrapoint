using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Judges;

/// <summary>
/// HTTP-level tests for the NotifyJudges rate limit (fixed window, partitioned per organizer `sub`).
/// Each test uses fresh organizer subs so the shared host's limiter state never leaks between tests.
/// </summary>
public sealed class NotifyJudgesRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const int PermitLimit = 2; // FR-065 default

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
        for (var i = 0; i < PermitLimit; i++)
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
        for (var i = 0; i < PermitLimit + 1; i++)
        {
            using var response = await NotifyJudgesAsync(anonymous, Guid.NewGuid());
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        await ExhaustNotifyBudgetAsync(organizer, competitionId);
    }

    private static async Task<List<Guid>> RegisterJudgesAsync(HttpClient client, Guid competitionId, int count)
    {
        var emails = Enumerable.Range(0, count).Select(_ => $"rl-{Guid.NewGuid():N}@brew.example").ToArray();
        using var register = await client.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/judges", new { emails }, TestContext.Current.CancellationToken);
        register.EnsureSuccessStatusCode();
        using var list = await client.GetAsync(
            Urls.Rel($"/api/v1/competitions/{competitionId}/judges"), TestContext.Current.CancellationToken);
        var json = await list.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return json.EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).ToList();
    }

    /// <summary>SendInvitation jobs for the competition regardless of status (the in-host worker may
    /// already have completed them), grouped by the judge id found in PayloadJson.</summary>
    private async Task<Dictionary<Guid, int>> SendInvitationJobCountsAsync(Guid competitionId, IReadOnlyCollection<Guid> judgeIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var payloads = await db.DispatchJobs
            .Where(j => j.CompetitionId == competitionId && j.Type == DispatchJobType.SendInvitation)
            .Select(j => j.PayloadJson)
            .ToListAsync(TestContext.Current.CancellationToken);
        return judgeIds.ToDictionary(
            id => id,
            id => payloads.Count(p => p.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<int> QueuedCountAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return json.GetProperty("queued").GetArrayLength();
    }

    [Fact]
    public async Task BackToBackNotifiesEnqueueExactlyOneSendInvitationPerJudge()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var judgeIds = await RegisterJudgesAsync(organizer, competitionId, 2);

        using var first = await NotifyJudgesAsync(organizer, competitionId);
        using var second = await NotifyJudgesAsync(organizer, competitionId);

        Assert.Equal(2, await QueuedCountAsync(first));
        Assert.Equal(0, await QueuedCountAsync(second));
        var counts = await SendInvitationJobCountsAsync(competitionId, judgeIds);
        Assert.All(counts, kv => Assert.Equal(1, kv.Value));
    }

    [Fact]
    public async Task ConcurrentNotifiesEnqueueExactlyOneSendInvitationPerJudge()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var judgeIds = await RegisterJudgesAsync(organizer, competitionId, 3);

        var responses = await Task.WhenAll(
            NotifyJudgesAsync(organizer, competitionId),
            NotifyJudgesAsync(organizer, competitionId));
        foreach (var r in responses)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        var totalQueued = 0;
        foreach (var r in responses)
        {
            totalQueued += await QueuedCountAsync(r);
            r.Dispose();
        }

        Assert.Equal(3, totalQueued);
        var counts = await SendInvitationJobCountsAsync(competitionId, judgeIds);
        Assert.All(counts, kv => Assert.Equal(1, kv.Value));
    }

    [Fact]
    public async Task RejectedNotifyReturnsRateLimitedProblemWithRetryAfterAndEnqueuesNothing()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await RegisterJudgesAsync(organizer, competitionId, 1);
        for (var i = 0; i < PermitLimit; i++)
        {
            using var ok = await NotifyJudgesAsync(organizer, competitionId);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        // A judge registered after the budget is spent would be queued if the call were not rejected.
        var late = await RegisterJudgesAsync(organizer, competitionId, 1);
        var before = await SendInvitationJobCountsAsync(competitionId, late);
        using var rejected = await NotifyJudgesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("urn:birrapoint:rate-limited", problem.GetProperty("type").GetString());
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var values));
        Assert.True(int.TryParse(values.Single(), out var seconds) && seconds > 0);

        var after = await SendInvitationJobCountsAsync(competitionId, late);
        Assert.Equal(before, after);
    }
}
