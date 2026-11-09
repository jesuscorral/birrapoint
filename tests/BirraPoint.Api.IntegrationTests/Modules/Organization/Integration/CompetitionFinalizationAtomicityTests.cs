using System.Data.Common;
using System.Net;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.IntegrationEvents;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Integration;

/// <summary>
/// The <c>competition-finalized</c> event is stored in the same transaction as the state change (FR-077): when
/// that transaction cannot commit, the competition stays <c>InEvaluation</c> and there is no outbox row and no
/// staged job. Publication no longer takes part in the transaction, so a failing subscriber is a delivery matter
/// (retry and dead letter, covered by the messaging block and host dead-letter tests), not an atomicity one.
/// </summary>
public sealed class CompetitionFinalizationAtomicityTests(ApiFactory factory) : IntegrationEventsTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task AFinalizeWhoseTransactionFailsToCommitLeavesNoStateChangeNoOutboxRowAndNoJob()
    {
        await using var host = NewHost(configureServices: services =>
            services.ConfigureDbContext<BirraPoint.Api.Common.Persistence.AppDbContext>(
                options => options.AddInterceptors(new FailFinalizeCommitInterceptor())));
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var closed = await SeedClosedCompetitionAsync(host, organizer, "Atomic");

        var response = await TransitionAsync(organizer, closed.CompetitionId, "Finalized");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("InEvaluation", (string)(await MessagingDb.ScalarAsync(
            ConnectionString, "SELECT \"State\" FROM organization.\"Competitions\" WHERE \"Id\" = @id", ("id", closed.CompetitionId)))!);
        Assert.Empty(await MessagingDb.OutboxAsync(
            ConnectionString, MessagingDb.Organization, MessagingDb.CompetitionFinalized, closed.CompetitionId));

        // The dispatcher is on: a surviving row would have produced a job within its poll interval.
        await EventualAssert.OutboxDrainedAsync(ConnectionString);
        Assert.Equal(0, await MessagingDb.JobCountAsync(ConnectionString, closed.CompetitionId, DispatchJobType.GeneratePdfs));
    }

    /// <summary>Fails the commit of the transaction that carries a <c>Finalized</c> competition, after every
    /// statement of the unit of work (state change, audit, outbox row) has already been executed in it.</summary>
    private sealed class FailFinalizeCommitInterceptor : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            if (eventData.Context is { } context
                && context.ChangeTracker.Entries<Competition>().Any(e => e.Entity.State == CompetitionState.Finalized))
            {
                throw new InvalidOperationException("Simulated commit failure.");
            }

            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }
}
