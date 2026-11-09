using System.IO.Compression;
using System.Net;
using System.Text.Json;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Domain;
using BuildingBlocks.EmailSender;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>
/// Correcting an evaluation of a finalized competition refreshes the already generated results (FR-083, US21):
/// <c>judging.evaluation-corrected</c> makes Publication stage a <c>RefreshResults</c> job that regenerates the
/// entry's PDF and rebuilds the ZIP without emailing participants again. Before finalizing nothing is published.
/// </summary>
public sealed class EvaluationCorrectedFlowTests(ApiFactory factory) : IntegrationEventsTestBase(factory), IClassFixture<ApiFactory>
{
    private const string RefreshResults = "RefreshResults";

    private sealed record Sheet(DateTimeOffset GeneratedAt, byte[] Bytes);

    private async Task<Sheet> ReadSheetAsync(Guid entryId)
    {
        await using var connection = await MessagingDb.OpenAsync(ConnectionString);
        await using var command = new NpgsqlCommand(
            "SELECT \"GeneratedAt\", \"PdfBytes\" FROM publication.\"GeneratedScoreSheets\" WHERE \"BeerEntryId\" = @id", connection);
        command.Parameters.AddWithValue("id", entryId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), "No generated score sheet for the entry.");
        return new Sheet(await reader.GetFieldValueAsync<DateTimeOffset>(0, TestContext.Current.CancellationToken), (byte[])reader.GetValue(1));
    }

    private async Task<Sheet> ReadArchiveAsync(Guid competitionId)
    {
        await using var connection = await MessagingDb.OpenAsync(ConnectionString);
        await using var command = new NpgsqlCommand(
            "SELECT \"GeneratedAt\", \"ZipBytes\" FROM publication.\"ResultsArchives\" WHERE \"CompetitionId\" = @id", connection);
        command.Parameters.AddWithValue("id", competitionId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken), "No results archive for the competition.");
        return new Sheet(await reader.GetFieldValueAsync<DateTimeOffset>(0, TestContext.Current.CancellationToken), (byte[])reader.GetValue(1));
    }

    private async Task<string?> RefreshStatusAsync(Guid competitionId) =>
        (string?)await MessagingDb.ScalarAsync(
            ConnectionString,
            "SELECT \"Status\" FROM jobs.\"DispatchJobs\" WHERE \"CompetitionId\" = @group AND \"Type\" = @type",
            ("group", competitionId), ("type", RefreshResults));

    private Task AwaitRefreshCompletedAsync(Guid competitionId) =>
        EventualAssert.EqualAsync(
            "Completed", async () => await RefreshStatusAsync(competitionId) ?? "none", "RefreshResults job completed");

    private static async Task AwaitDispatchCompletedAsync(HttpClient organizer, Guid competitionId, int expectedRows)
    {
        await EventualAssert.TrueAsync(async () =>
        {
            var response = await organizer.GetAsync(
                Urls.Rel($"/api/v1/competitions/{competitionId}/dispatch"), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var rows = document.RootElement.EnumerateArray().ToList();
            return rows.Count == expectedRows && rows.All(r => r.GetProperty("status").GetString() == "Completed");
        }, "initial results dispatch completed", EventualAssert.PipelineTimeout);
    }

    [Fact]
    public async Task CorrectingAFinalizedCompetitionRefreshesTheEntrySheetAndTheArchiveWithoutEmailingAgain()
    {
        await using var host = NewHost();
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var closed = await SeedClosedCompetitionAsync(host, organizer, "Refresh");
        var entry = closed.Entries[0];
        Assert.Equal(HttpStatusCode.OK, (await TransitionAsync(organizer, closed.CompetitionId, "Finalized")).StatusCode);
        await AwaitDispatchCompletedAsync(organizer, closed.CompetitionId, expectedRows: 1);

        var sheetBefore = await ReadSheetAsync(entry.EntryId);
        var archiveBefore = await ReadArchiveAsync(closed.CompetitionId);
        var sender = (FakeEmailSender)host.Services.GetRequiredService<IEmailSender>();
        var emailsBefore = sender.Sent.Count;
        var emailJobsBefore = await MessagingDb.JobCountAsync(ConnectionString, closed.CompetitionId, DispatchJobType.SendResultEmail);

        var response = await CorrectEvaluationAsync(organizer, closed.CompetitionId, entry.EvaluationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evt = Assert.Single(await MessagingDb.OutboxAsync(
            ConnectionString, MessagingDb.Judging, MessagingDb.EvaluationCorrected, closed.CompetitionId));
        Assert.Equal(entry.EntryId, evt.Property("beerEntryId").GetGuid());
        Assert.Equal(entry.EvaluationId, evt.Property("evaluationId").GetGuid());

        await AwaitRefreshCompletedAsync(closed.CompetitionId);
        Assert.Equal(1, await MessagingDb.JobCountAsync(ConnectionString, closed.CompetitionId, RefreshResults));

        var sheetAfter = await ReadSheetAsync(entry.EntryId);
        Assert.True(sheetAfter.GeneratedAt > sheetBefore.GeneratedAt, "The entry's PDF was not regenerated.");
        Assert.NotEqual(sheetBefore.Bytes, sheetAfter.Bytes);

        var archiveAfter = await ReadArchiveAsync(closed.CompetitionId);
        Assert.True(archiveAfter.GeneratedAt > archiveBefore.GeneratedAt, "The results ZIP was not rebuilt.");
        using (var zip = new ZipArchive(new MemoryStream(archiveAfter.Bytes), ZipArchiveMode.Read))
        {
            var found = false;
            foreach (var zipEntry in zip.Entries)
            {
                using var stream = await zipEntry.OpenAsync(TestContext.Current.CancellationToken);
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);
                found |= copy.ToArray().AsSpan().SequenceEqual(sheetAfter.Bytes);
            }

            Assert.True(found, "The rebuilt ZIP does not contain the regenerated PDF with the corrected total.");
        }

        // No participant is emailed again.
        Assert.Equal(emailJobsBefore, await MessagingDb.JobCountAsync(ConnectionString, closed.CompetitionId, DispatchJobType.SendResultEmail));
        Assert.Equal(emailsBefore, sender.Sent.Count);
    }

    [Fact]
    public async Task CorrectingBeforeFinalizingPublishesNoEventAndStagesNoJob()
    {
        await using var host = NewHost();
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var closed = await SeedClosedCompetitionAsync(host, organizer, "NoRefresh");

        var response = await CorrectEvaluationAsync(organizer, closed.CompetitionId, closed.Entries[0].EvaluationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await MessagingDb.OutboxAsync(
            ConnectionString, MessagingDb.Judging, MessagingDb.EvaluationCorrected, closed.CompetitionId));
        await EventualAssert.OutboxDrainedAsync(ConnectionString);
        Assert.Equal(0, await MessagingDb.JobCountAsync(ConnectionString, closed.CompetitionId, RefreshResults));
    }

    [Fact]
    public async Task WhenNoSheetOrArchiveExistsYetTheRefreshDoesNothingAndCompletes()
    {
        await using var host = NewHost();
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var closed = await SeedClosedCompetitionAsync(host, organizer, "RefreshEmpty");
        // Finalized without the dispatch having produced anything yet.
        await SeedCompetitionStateAsync(host.Services, closed.CompetitionId, CompetitionState.Finalized);

        var response = await CorrectEvaluationAsync(organizer, closed.CompetitionId, closed.Entries[0].EvaluationId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AwaitRefreshCompletedAsync(closed.CompetitionId);
        Assert.Equal(0, await MessagingDb.CountAsync(
            ConnectionString, "SELECT count(*) FROM publication.\"GeneratedScoreSheets\" WHERE \"BeerEntryId\" = @id",
            ("id", closed.Entries[0].EntryId)));
        Assert.Equal(0, await MessagingDb.CountAsync(
            ConnectionString, "SELECT count(*) FROM publication.\"ResultsArchives\" WHERE \"CompetitionId\" = @id",
            ("id", closed.CompetitionId)));
    }

    [Fact]
    public async Task RedeliveringTheCorrectionEventThreeTimesStagesOneJob()
    {
        await using var host = NewHost();
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var closed = await SeedClosedCompetitionAsync(host, organizer, "RefreshRedelivery");
        await SeedCompetitionStateAsync(host.Services, closed.CompetitionId, CompetitionState.Finalized);
        await CorrectEvaluationAsync(organizer, closed.CompetitionId, closed.Entries[0].EvaluationId);
        var evt = Assert.Single(await MessagingDb.OutboxAsync(
            ConnectionString, MessagingDb.Judging, MessagingDb.EvaluationCorrected, closed.CompetitionId));
        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Judging, evt.Id);
        await AwaitRefreshCompletedAsync(closed.CompetitionId);

        for (var redelivery = 0; redelivery < 3; redelivery++)
        {
            await MessagingDb.ForceRedeliveryAsync(ConnectionString, MessagingDb.Judging, evt.Id);
            await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Judging, evt.Id);
        }

        Assert.Equal(1, await MessagingDb.JobCountAsync(ConnectionString, closed.CompetitionId, RefreshResults));
        Assert.Equal(1, await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Publication, evt.Id));
    }
}
