using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts.Events;
using BirraPoint.Api.Modules.Organization.Domain;
using MediatR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Integration;

/// <summary>Finalizing publishes CompetitionFinalized inside the same unit of work that commits the
/// state change: a failing subscriber must leave the competition InEvaluation with no staged job.
/// (The success path, including the GeneratePdfs job, is covered by DispatchPipelineApiTests.)</summary>
public sealed class CompetitionFinalizationAtomicityTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AFailingCompetitionFinalizedHandlerRollsBackTheStateChangeAndStagesNoJob()
    {
        using var throwingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddTransient<INotificationHandler<CompetitionFinalized>>(_ => new ThrowingHandler())));
        using var client = throwingFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken($"organizer-{Guid.NewGuid():N}", null, "ORGANIZER"));

        var created = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = $"Atomic {Guid.NewGuid():N}",
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        }, TestContext.Current.CancellationToken);
        var competitionId = (await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Active" }, TestContext.Current.CancellationToken);
        await client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "InEvaluation" }, TestContext.Current.CancellationToken);

        var response = await client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Finalized" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var competition = await db.Competitions.AsNoTracking().SingleAsync(c => c.Id == competitionId, TestContext.Current.CancellationToken);
        Assert.Equal(CompetitionState.InEvaluation, competition.State);
        Assert.False(await db.DispatchJobs.AnyAsync(j => j.CompetitionId == competitionId, TestContext.Current.CancellationToken));
    }

    private sealed class ThrowingHandler : INotificationHandler<CompetitionFinalized>
    {
        public Task Handle(CompetitionFinalized notification, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated subscriber failure.");
    }
}
