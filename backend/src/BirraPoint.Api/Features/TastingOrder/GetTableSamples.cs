using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;

namespace BirraPoint.Api.Features.TastingOrder;

/// <summary>GET /me/tables/{tableId}/samples (contracts/rest-api.md §Judge workspace). Returns
/// null when the caller is not an active member of this table — the endpoint maps that to a
/// plain 404 (never reveals whether the table exists).</summary>
public sealed record GetTableSamplesQuery(Guid TableId) : IRequest<IReadOnlyList<JudgeSampleDto>?>;

public sealed class GetTableSamplesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetTableSamplesQuery, IReadOnlyList<JudgeSampleDto>?>
{
    public async Task<IReadOnlyList<JudgeSampleDto>?> Handle(GetTableSamplesQuery request, CancellationToken cancellationToken)
    {
        var judges = await currentUser.GetJudgeRecordsAsync(cancellationToken);
        var judgeId = await JudgeTableAccess.FindActiveMembershipAsync(dbContext, judges, request.TableId, cancellationToken);

        return judgeId is null
            ? null
            : await JudgeSampleProjector.ProjectAsync(dbContext, request.TableId, judgeId.Value, cancellationToken);
    }
}
