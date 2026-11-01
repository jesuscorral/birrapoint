using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Features.TastingOrder.CommonComponents;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.TastingOrder.Features.FixOrder;

internal sealed class FixOrderCommandValidator : AbstractValidator<FixOrderCommand>
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
