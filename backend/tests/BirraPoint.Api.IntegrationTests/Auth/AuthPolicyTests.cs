using System.Net;
using System.Net.Http.Headers;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Auth;

/// <summary>
/// T021: proves the deny-by-default fallback policy (401), the ORGANIZER role policy (403), and
/// the owner-scoped 404 convention (no cross-owner existence leak, rest-api.md §Error catalog)
/// over real HTTP. No real ORGANIZER + owner-scoped REST endpoint exists on this branch yet, so
/// <see cref="TestOnlyAuthorizationEndpointsStartupFilter"/> supplies a diagnostic-only stand-in
/// that exercises the exact same policy/ownership pattern as
/// <c>CompetitionHub.JoinCompetitionAsOrganizer</c>.
/// </summary>
public sealed class AuthPolicyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Request_without_a_bearer_token_is_rejected_with_401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/styles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Judge_token_on_organizer_only_endpoint_is_forbidden_with_403()
    {
        using var client = TestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: "kc-judge-1", roles: ["JUDGE"]));

        var response = await client.GetAsync("/__test/organizer-only");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Organizer_token_for_a_competition_they_do_not_own_returns_404()
    {
        var competitionId = await SeedCompetitionOwnedByAsync("sub-owner-a");

        using var client = TestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: "sub-owner-b", roles: ["ORGANIZER"]));

        var response = await client.GetAsync($"/__test/organizer-only/competitions/{competitionId}");

        // Explicitly not 403: an organizer role holder must never learn (via a distinguishable
        // status code) that a competition they don't own exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Organizer_token_for_their_own_competition_succeeds()
    {
        const string ownerSub = "sub-owner-c";
        var competitionId = await SeedCompetitionOwnedByAsync(ownerSub);

        using var client = TestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: ownerSub, roles: ["ORGANIZER"]));

        var response = await client.GetAsync($"/__test/organizer-only/competitions/{competitionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient TestClient() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter, TestOnlyAuthorizationEndpointsStartupFilter>()))
            .CreateClient();

    private async Task<Guid> SeedCompetitionOwnedByAsync(string ownerSub)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var competition = new Competition
        {
            Name = $"Auth policy test {Guid.NewGuid()}",
            Venue = "Test venue",
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            CreatedByUserId = ownerSub,
        };
        db.Competitions.Add(competition);
        await db.SaveChangesAsync();

        return competition.Id;
    }
}
