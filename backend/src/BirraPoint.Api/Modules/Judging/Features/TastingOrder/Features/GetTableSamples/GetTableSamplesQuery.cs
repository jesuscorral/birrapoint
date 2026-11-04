using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Organization.Contracts;
using MediatR;

namespace BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.GetTableSamples;

/// <summary>GET /me/tables/{tableId}/samples (contracts/rest-api.md §Judge workspace). Returns
/// null when the caller is not an active member of this table — the endpoint maps that to a
/// plain 404 (never reveals whether the table exists).</summary>
internal sealed record GetTableSamplesQuery(Guid TableId) : IRequest<IReadOnlyList<JudgeSampleDto>?>;

internal sealed class GetTableSamplesQueryHandler(AppDbContext dbContext, IJudgeDirectory judgeDirectory, IStyleCatalog styleCatalog, IEntryDirectory entryDirectory)
    : IRequestHandler<GetTableSamplesQuery, IReadOnlyList<JudgeSampleDto>?>
{
    public async Task<IReadOnlyList<JudgeSampleDto>?> Handle(GetTableSamplesQuery request, CancellationToken cancellationToken)
    {
        var judges = await judgeDirectory.GetSignedInJudgesAsync(cancellationToken);
        var judgeId = await JudgeTableAccess.FindActiveMembershipAsync(dbContext, judges, request.TableId, cancellationToken);

        return judgeId is null
            ? null
            : await JudgeSampleProjector.ProjectAsync(dbContext, styleCatalog, entryDirectory, request.TableId, judgeId.Value, cancellationToken);
    }
}
