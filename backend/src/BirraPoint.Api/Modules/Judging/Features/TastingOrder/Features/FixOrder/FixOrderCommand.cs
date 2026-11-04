using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Realtime;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.FixOrder;

internal sealed record FixOrderRequest(IReadOnlyList<Guid> OrderedBeerEntryIds);

/// <summary>POST /me/tables/{tableId}/order (contracts/rest-api.md §Judge workspace, US6-4).
/// Returns null when the caller is not an active member of this table — the endpoint maps that
/// to a plain 404.</summary>
internal sealed record FixOrderCommand(Guid TableId, IReadOnlyList<Guid> OrderedBeerEntryIds)
    : IRequest<IReadOnlyList<JudgeSampleDto>?>;

internal sealed class FixOrderCommandHandler(
    AppDbContext dbContext, IJudgeDirectory judgeDirectory, ICompetitionAccess competitionAccess, IEventPublisher eventPublisher, IStyleCatalog styleCatalog, IEntryDirectory entryDirectory)
    : IRequestHandler<FixOrderCommand, IReadOnlyList<JudgeSampleDto>?>
{
    public async Task<IReadOnlyList<JudgeSampleDto>?> Handle(FixOrderCommand request, CancellationToken cancellationToken)
    {
        var judges = await judgeDirectory.GetSignedInJudgesAsync(cancellationToken);
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

        var competitionState = (await competitionAccess.GetAsync(table.CompetitionId, cancellationToken)
            ?? throw new InvalidOperationException("Competition of an existing table not found.")).State;

        var isAlreadyFixed = table.OrderFixedByJudgeId is not null;
        var fixerName = table.OrderFixedByJudgeId is { } fixerId
            ? (await judgeDirectory.GetDisplayNamesAsync([fixerId], cancellationToken)).GetValueOrDefault(fixerId)
            : null;

        TastingOrderRules.EnsureOrderCanBeFixed(isAlreadyFixed, fixerName, competitionState);

        var samples = await dbContext.TableSamples
            .Where(ts => ts.TastingTableId == request.TableId)
            .ToListAsync(cancellationToken);

        // Re-validate the permutation against the table's samples as of the row lock, not just the
        // pre-lock snapshot the validator saw — an organizer edit to the table's membership
        // (Modules/Judging/Features/Tables, still allowed while the competition is Active) could otherwise race
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

        var result = await JudgeSampleProjector.ProjectAsync(dbContext, styleCatalog, entryDirectory, request.TableId, judgeId.Value, cancellationToken);
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
