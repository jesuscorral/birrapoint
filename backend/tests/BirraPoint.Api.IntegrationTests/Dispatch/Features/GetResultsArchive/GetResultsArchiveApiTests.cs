using System.Net;
using System.Text.Json;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Dispatch.Features.GetResultsArchive;

public sealed class GetResultsArchiveApiTests(ApiFactory factory) : DispatchApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetResultsArchiveBeforeThePipelineCompletesReturns202WithStatus()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer, "Pending");

        // A BundleZip job stuck Running is never touched by DispatchWorker's periodic sweep
        // (only Pending jobs are dispatched, and only a startup resume-sweep touches Running ones)
        // — this keeps the "not ready yet" state deterministic for the lifetime of this test.
        await SeedDispatchJobAsync(competitionId, DispatchJobType.BundleZip, DispatchJobStatus.Running, new { });

        var response = await GetResultsArchiveAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Running", document.RootElement.GetProperty("status").GetString());
    }
}
