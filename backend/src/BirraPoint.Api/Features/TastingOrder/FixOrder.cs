using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Realtime;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.TastingOrder;

/// <summary>POST /me/tables/{tableId}/order (contracts/rest-api.md §Judge workspace, US6-4).
/// Returns null when the caller is not an active member of this table — the endpoint maps that
/// to a plain 404.</summary>
public sealed record FixOrderCommand(Guid TableId, IReadOnlyList<Guid> OrderedBeerEntryIds)
    : IRequest<IReadOnlyList<JudgeSampleDto>?>;

public sealed class FixOrderCommandValidator : AbstractValidator<FixOrderCommand>
{
    public FixOrderCommandValidator(AppDbContext dbContext, ICurrentUser currentUser)
    {
        RuleFor(c => c.OrderedBeerEntryIds).NotNull();

        RuleFor(c => c)
            .MustAsync(async (command, cancellationToken) =>
            {
                var judges = await currentUser.GetJudgeRecordsAsync(cancellationToken);
                var judgeId = await JudgeTableAccess.FindActiveMembershipAsync(
                    dbContext, judges, command.TableId, cancellationToken);
                if (judgeId is null)
                {
                    // Not a member — the handler 404s instead; failing validation here would leak
                    // this table's existence to a non-member (Features/Tables/TableValidationRules
                    // ownership-scoping convention).
                    return true;
                }

                var currentSampleIds = await dbContext.TableSamples
                    .Where(ts => ts.TastingTableId == command.TableId)
                    .Select(ts => ts.BeerEntryId)
                    .ToListAsync(cancellationToken);

                return TastingOrderRules.IsExactPermutation(command.OrderedBeerEntryIds, currentSampleIds);
            })
            .WithName(nameof(FixOrderCommand.OrderedBeerEntryIds))
            .WithMessage("orderedBeerEntryIds must be an exact permutation of the table's current samples, with no duplicates.")
            .When(c => c.OrderedBeerEntryIds is not null);
    }
}

public sealed class FixOrderCommandHandler(AppDbContext dbContext, ICurrentUser currentUser, IEventPublisher eventPublisher)
    : IRequestHandler<FixOrderCommand, IReadOnlyList<JudgeSampleDto>?>
{
    public async Task<IReadOnlyList<JudgeSampleDto>?> Handle(FixOrderCommand request, CancellationToken cancellationToken)
    {
        var judges = await currentUser.GetJudgeRecordsAsync(cancellationToken);
        var judgeId = await JudgeTableAccess.FindActiveMembershipAsync(dbContext, judges, request.TableId, cancellationToken);
        if (judgeId is null)
        {
            return null;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Row lock to serialize concurrent fixers — the one-shot guarantee (US6-4 / Clarification
        // Q1) must hold even when two judges race POST .../order at the same instant.
        var table = await dbContext.TastingTables
            .FromSqlInterpolated($"SELECT * FROM \"TastingTables\" WHERE \"Id\" = {request.TableId} FOR UPDATE")
            .SingleAsync(cancellationToken);

        var competitionState = await dbContext.Competitions
            .Where(c => c.Id == table.CompetitionId)
            .Select(c => c.State)
            .SingleAsync(cancellationToken);

        var isAlreadyFixed = table.OrderFixedByJudgeId is not null;
        var fixerName = isAlreadyFixed
            ? await dbContext.Judges
                .Where(j => j.Id == table.OrderFixedByJudgeId)
                .Select(j => j.DisplayName)
                .SingleOrDefaultAsync(cancellationToken)
            : null;

        TastingOrderRules.EnsureOrderCanBeFixed(isAlreadyFixed, fixerName, competitionState);

        var samples = await dbContext.TableSamples
            .Where(ts => ts.TastingTableId == request.TableId)
            .ToListAsync(cancellationToken);

        // Re-validate the permutation against the table's samples as of the row lock, not just the
        // pre-lock snapshot the validator saw — an organizer edit to the table's membership
        // (Features/Tables, still allowed while the competition is Active) could otherwise race
        // this call and leave sequenceByEntryId missing an entry, throwing an unhandled
        // KeyNotFoundException (500) instead of a clean, retryable error.
        var currentSampleIds = samples.Select(s => s.BeerEntryId).ToList();
        if (!TastingOrderRules.IsExactPermutation(request.OrderedBeerEntryIds, currentSampleIds))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "The table's samples changed while this order was being fixed; reload and try again.");
        }

        var sequenceByEntryId = request.OrderedBeerEntryIds
            .Select((entryId, index) => (entryId, sequence: index + 1))
            .ToDictionary(x => x.entryId, x => x.sequence);

        foreach (var sample in samples)
        {
            sample.SequenceOrder = sequenceByEntryId[sample.BeerEntryId];
        }

        table.OrderFixedByJudgeId = judgeId;
        table.OrderFixedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = await JudgeSampleProjector.ProjectAsync(dbContext, request.TableId, judgeId.Value, cancellationToken);
        var fixedByDisplayName = judges.First(j => j.Id == judgeId).DisplayName;

        // Emitted only after the transaction above commits (contracts/signalr-hub.md §Delivery
        // semantics) — never before, to avoid phantom events from a rolled-back fix. Same payload
        // shape to both groups (contracts/signalr-hub.md documents the organizer-group event as
        // "same as judge event") — the dashboard needs to know a table's order was fixed too.
        var orderedSamples = result.Select(s => new
        {
            beerEntryId = s.BeerEntryId,
            blindCode = s.BlindCode,
            sequenceOrder = s.SequenceOrder,
        });

        await eventPublisher.PublishToTableAsync(
            request.TableId,
            "TableOrderFixed",
            new { tableId = request.TableId, orderedSamples, fixedByDisplayName },
            CancellationToken.None);

        await eventPublisher.PublishToOrganizersAsync(
            table.CompetitionId,
            "TableOrderFixed",
            new { tableId = request.TableId, orderedSamples, fixedByDisplayName },
            CancellationToken.None);

        return result;
    }
}
