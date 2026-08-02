using System.Text.Json;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Payload shape enqueued by RegisterJudges/ConsolidateJudgeImport — no secrets (the temp password is generated fresh inside HandleAsync and immediately discarded).</summary>
public sealed record ProvisionJudgeAccountPayload(Guid JudgeId);

/// <summary>
/// R-20: creates (or reuses) the judge's Keycloak account eagerly, without sending an email —
/// <see cref="SendInvitationHandler"/> performs the actual notification later, only on the
/// organizer's explicit "Notify judges" action. Never persists the returned temporary password
/// (R-10's "no secrets" invariant) — the later SendInvitation job generates and emails its own
/// fresh one via the same idempotent <c>EnsureUserWithTemporaryPasswordAsync</c> call. No
/// try/catch: a thrown exception propagates to DispatchWorker's existing retry/backoff
/// (DispatchRetryPolicy), same as every other IDispatchJobHandler.
/// </summary>
/// <remarks>
/// Skips provisioning once the judge's invitation has left <see cref="InvitationStatus.Pending"/>
/// — a password has already been issued (or attempted) via <see cref="SendInvitationHandler"/>, so
/// calling <c>EnsureUserWithTemporaryPasswordAsync</c> again would reset it out from under the
/// judge with no recovery path (re-import consolidation and stale retries both re-enqueue this
/// job for already-known judges; see research.md R-20).
/// </remarks>
public sealed class ProvisionJudgeAccountHandler(AppDbContext dbContext, IKeycloakAdminClient keycloakAdminClient)
    : IDispatchJobHandler
{
    public DispatchJobType Type => DispatchJobType.ProvisionJudgeAccount;

    public async Task HandleAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ProvisionJudgeAccountPayload>(job.PayloadJson)
            ?? throw new InvalidOperationException($"DispatchJob {job.Id} has an empty ProvisionJudgeAccount payload.");

        var judge = await dbContext.Judges.FirstOrDefaultAsync(j => j.Id == payload.JudgeId, cancellationToken)
            ?? throw new InvalidOperationException($"Judge {payload.JudgeId} not found for DispatchJob {job.Id}.");

        var invitationPending = await dbContext.Invitations.AnyAsync(
            i => i.JudgeId == judge.Id && i.Status == InvitationStatus.Pending, cancellationToken);
        if (!invitationPending)
        {
            return;
        }

        await keycloakAdminClient.EnsureUserWithTemporaryPasswordAsync(judge.Email, cancellationToken);
    }
}
