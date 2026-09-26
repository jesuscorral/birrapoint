using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Evaluations;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Evaluations;

/// <summary>
/// Deterministic reproduction of the race described in <c>SubmitEvaluationCommandHandler</c>:
/// right after the handler's early idempotent-replay check finds nothing for (judge, entry), this
/// hook inserts and commits — on its own <see cref="AppDbContext"/>/connection, exactly as a truly
/// concurrent second request would — a fully-formed <see cref="Evaluation"/> row for that same
/// pair, before the handler's own execution reaches the <c>alreadySubmittedIds</c> query moments
/// later. This turns the race into a guaranteed, single-execution scenario instead of relying on
/// two real concurrent HTTP calls happening to interleave in the exact right window (flaky).
/// </summary>
public sealed class ConcurrentInsertRaceTestHook(IServiceScopeFactory scopeFactory) : IEvaluationRaceTestHook
{
    private const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

    /// <summary>Id of the row this hook inserted — the handler's response is expected to echo it
    /// back (idempotent replay), not mint a new one.</summary>
    public Guid? InsertedEvaluationId { get; private set; }

    public async Task AfterEarlyReplayCheckAsync(Guid tableId, Guid judgeId, Guid beerEntryId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var evaluation = new Evaluation
        {
            TastingTableId = tableId,
            JudgeId = judgeId,
            BeerEntryId = beerEntryId,
            AromaScore = 9,
            AppearanceScore = 3,
            FlavorScore = 18,
            MouthfeelScore = 4,
            OverallScore = 9,
            AromaComment = LongComment,
            AppearanceComment = LongComment,
            FlavorComment = LongComment,
            MouthfeelComment = LongComment,
            OverallComment = LongComment,
            Status = EvaluationStatus.Confirmed,
            SubmittedAt = DateTimeOffset.UtcNow,
        };

        db.Evaluations.Add(evaluation);
        await db.SaveChangesAsync(cancellationToken);

        InsertedEvaluationId = evaluation.Id;
    }
}
