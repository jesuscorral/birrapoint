using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.TastingOrder;

/// <summary>GET /me/tables/{tableId}/judges (contracts/rest-api.md §Judge workspace). Returns
/// null when the caller is not an active member of this table — the endpoint maps that to a
/// plain 404 (never reveals whether the table exists), same convention as GetTableSamples.</summary>
public sealed record GetTableJudgesQuery(Guid TableId) : IRequest<IReadOnlyList<JudgeTableMemberDto>?>;

public sealed class GetTableJudgesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetTableJudgesQuery, IReadOnlyList<JudgeTableMemberDto>?>
{
    public async Task<IReadOnlyList<JudgeTableMemberDto>?> Handle(GetTableJudgesQuery request, CancellationToken cancellationToken)
    {
        var judges = await currentUser.GetJudgeRecordsAsync(cancellationToken);
        var callerJudgeId = await JudgeTableAccess.FindActiveMembershipAsync(dbContext, judges, request.TableId, cancellationToken);
        if (callerJudgeId is null)
        {
            return null;
        }

        // Informational only (no editing capability hangs off this) — every other actively
        // assigned judge at the table, excluding the caller themselves. Ordered client-side
        // (StringComparer.Ordinal isn't SQL-translatable) after materializing the query.
        var members = await dbContext.TableJudges
            .Where(tj => tj.TastingTableId == request.TableId && tj.RemovedAt == null && tj.JudgeId != callerJudgeId)
            .Join(dbContext.Judges, tj => tj.JudgeId, j => j.Id, (_, j) => new JudgeTableMemberDto(j.DisplayName, j.BjcpRank))
            .ToListAsync(cancellationToken);

        return members.OrderBy(m => m.DisplayName, StringComparer.Ordinal).ToList();
    }
}
