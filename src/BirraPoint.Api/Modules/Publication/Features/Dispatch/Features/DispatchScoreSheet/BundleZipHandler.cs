using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.CommonComponents;
using BuildingBlocks.Jobs;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.DispatchScoreSheet;

/// <summary>
/// FR-040: bundles the competition's score sheets into the results ZIP with <see cref="ResultsArchiveBuilder"/>, then enqueues one
/// <see cref="DispatchJobType.SendResultEmail"/> job per participant (FR-041) — each recipient's
/// delivery status/attempts/lastError live entirely on that job's own row, the same way
/// <see cref="Invitation"/> tracks judge invites, except here there's no separate status entity.
/// </summary>
internal sealed class BundleZipHandler(
    AppDbContext dbContext, ResultsArchiveBuilder archiveBuilder, IResultRecipients resultRecipients, IJobQueue dispatchJobQueue)
    : IJobHandler
{
    public string Type => DispatchJobType.BundleZip;

    public async Task HandleAsync(Job job, CancellationToken cancellationToken)
    {
        await archiveBuilder.BuildAsync(job.GroupKey, cancellationToken);

        var participantIds = (await resultRecipients.GetRecipientsAsync(job.GroupKey, cancellationToken))
            .Select(r => r.ParticipantId)
            .ToList();

        // IJobHandler is at-least-once (DispatchWorker may re-run this handler after a
        // crash or a transient failure marking THIS job Completed, even though the fan-out below
        // already committed durably on a prior attempt) — without this guard a re-run would
        // silently double-enqueue a full second round of SendResultEmail jobs, sending every
        // participant their results twice.
        var alreadyQueuedPayloads = await dbContext.DispatchJobs
            .OfDispatchType(DispatchJobType.SendResultEmail)
            .Where(j => j.GroupKey == job.GroupKey)
            .Select(j => j.PayloadJson)
            .ToListAsync(cancellationToken);
        var alreadyQueuedParticipantIds = alreadyQueuedPayloads
            .Select(payload => JsonSerializer.Deserialize<SendResultEmailPayload>(payload)!.ParticipantId)
            .ToHashSet();

        foreach (var participantId in participantIds.Where(id => !alreadyQueuedParticipantIds.Contains(id)))
        {
            await dispatchJobQueue.EnqueueAsync(
                job.GroupKey, DispatchJobType.SendResultEmail, new SendResultEmailPayload(participantId), cancellationToken);
        }
    }
}
