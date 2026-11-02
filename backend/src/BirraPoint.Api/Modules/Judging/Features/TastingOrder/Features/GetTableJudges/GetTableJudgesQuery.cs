using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.GetTableJudges;

/// <summary>GET /me/tables/{tableId}/judges (contracts/rest-api.md §Judge workspace). Returns
/// null when the caller is not an active member of this table — the endpoint maps that to a
/// plain 404 (never reveals whether the table exists), same convention as GetTableSamples.</summary>
internal sealed record GetTableJudgesQuery(Guid TableId) : IRequest<IReadOnlyList<JudgeTableMemberDto>?>;

internal sealed class GetTableJudgesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
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
        // assigned judge at the table, excluding the caller themselves. Ordered SQL-side by the
        // database's own collation, which (unlike StringComparer.Ordinal) sorts accented Spanish
        // names such as Álvaro/Ñuño correctly.
        return await dbContext.TableJudges
            .Where(tj => tj.TastingTableId == request.TableId && tj.RemovedAt == null && tj.JudgeId != callerJudgeId)
            .Join(dbContext.Judges, tj => tj.JudgeId, j => j.Id, (_, j) => j)
            .OrderBy(j => j.DisplayName)
            .Select(j => new JudgeTableMemberDto(j.DisplayName, j.BjcpRank))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>GET /me/tables/{tableId}/judges — the table's other active judges, informational only
/// (no editing capability hangs off this). BjcpRank is free text from the judge-roster import
/// (US14/FR-057), null for a judge created via the plain email-list flow.</summary>
internal sealed record JudgeTableMemberDto(string DisplayName, string? BjcpRank);
