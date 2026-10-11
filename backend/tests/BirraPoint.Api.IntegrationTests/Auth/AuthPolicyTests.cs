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
    public async Task RequestWithoutABearerTokenIsRejectedWith401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Urls.Rel("/api/v1/styles"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task JudgeTokenOnOrganizerOnlyEndpointIsForbiddenWith403()
    {
        using var client = TestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: "kc-judge-1", roles: ["JUDGE"]));

        var response = await client.GetAsync(Urls.Rel("/__test/organizer-only"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OrganizerTokenForACompetitionTheyDoNotOwnReturns404()
    {
        var competitionId = await SeedCompetitionOwnedByAsync("sub-owner-a");

        using var client = TestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: "sub-owner-b", roles: ["ORGANIZER"]));

        var response = await client.GetAsync(Urls.Rel($"/__test/organizer-only/competitions/{competitionId}"), TestContext.Current.CancellationToken);

        // Explicitly not 403: an organizer role holder must never learn (via a distinguishable
        // status code) that a competition they don't own exists.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OrganizerTokenForTheirOwnCompetitionSucceeds()
    {
        const string ownerSub = "sub-owner-c";
        var competitionId = await SeedCompetitionOwnedByAsync(ownerSub);

        using var client = TestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub: ownerSub, roles: ["ORGANIZER"]));

        var response = await client.GetAsync(Urls.Rel($"/__test/organizer-only/competitions/{competitionId}"), TestContext.Current.CancellationToken);

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
