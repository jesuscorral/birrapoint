using BirraPoint.Api.Common.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.IntegrationTests.Auth;

/// <summary>
/// T021: diagnostic-only endpoints under <c>/__test/</c>, registered exclusively inside
/// <see cref="AuthPolicyTests"/> via <c>ConfigureTestServices</c> — never shipped, never listed
/// in contracts/rest-api.md. This branch has no real ORGANIZER-only + owner-scoped REST endpoint
/// yet (that lives only in unmerged Phase 4 work), so these endpoints exercise the real
/// <c>"ORGANIZER"</c> authorization policy plus the same ownership-scoping check as
/// <c>CompetitionHub.JoinCompetitionAsOrganizer</c> (Realtime/CompetitionHub.cs), but through
/// ASP.NET Core's authorization middleware so 403 (wrong role) and 404 (right role, wrong owner)
/// are distinguishable HTTP outcomes — the hub throws the same HubException for both cases,
/// which is exactly why it can't be reused for this test.
/// </summary>
public sealed class TestOnlyAuthorizationEndpointsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGet("/__test/organizer-only", () => Results.Ok())
                .RequireAuthorization("ORGANIZER");

            endpoints.MapGet("/__test/organizer-only/competitions/{id:guid}",
                async (Guid id, HttpContext httpContext, AppDbContext db) =>
                {
                    var sub = httpContext.User.FindFirst("sub")?.Value;
                    var owns = await db.Competitions.AnyAsync(competition =>
                        competition.Id == id && competition.CreatedByUserId == sub);
                    return owns ? Results.Ok() : Results.NotFound();
                })
                .RequireAuthorization("ORGANIZER");
        });
    };
}
