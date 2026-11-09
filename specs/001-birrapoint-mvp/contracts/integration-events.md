# Contract: Integration events between modules

Modules notify each other through integration events (FR-076–FR-089, ADR-0042). An event is
stored in the publishing module's outbox in the same transaction as the change. A background
dispatcher delivers it at least once to every subscriber. Each subscriber records the event id in
its inbox, so a redelivery has no effect. The transport is the database for now; #108 swaps it for
Dapr pub/sub without changing these contracts.

## Envelope (every event)

| Field | Type | Notes |
|---|---|---|
| `id` | Guid (v7) | event id; inbox de-duplication key together with the handler name |
| `type` | string | `<module>.<kebab-name>`, stable; never derived from CLR names at runtime |
| `version` | int | starts at `1`; additive payload changes keep the version, breaking ones add a new version handled side by side |
| `occurredAt` | timestamptz | when the publishing transaction staged it |
| `payload` | JSON | shape below; identifiers only, no snapshots (receivers recompute from current state) |

Rules:
- Payloads never carry personal data (emails, names) or entrant fields (name, participant,
  brewery, origin). Events consumed by Judging are covered by contract tests (FR-085).
- Delivery order is not guaranteed, and every handler converges to the same state whatever the
  order (FR-079).
- A handler that finds its target gone or in a state that no longer allows the reaction does
  nothing and completes.

## Events

| Type (v1) | Publisher (outbox) | Payload | Subscribers (inbox): reaction |
|---|---|---|---|
| `organization.competition-finalized` | Organization: `PUT /competitions/{id}/state` to `Finalized` | `{ competitionId }` | Publication: stages `GeneratePdfs` (FR-036), wakes the job worker after commit |
| `organization.judge-email-changed` | Organization: `PUT /competitions/{id}/judges/{judgeId}` | `{ competitionId, judgeId }` | Judging: if the judge sits at a table holding an entry they now own, sends `JudgeConflictDetected` to the competition's organizers (FR-082). BOS flags are recomputed by Organization in the publishing transaction, not here. |
| `judging.table-judges-changed` | Judging: `POST /competitions/{id}/tables`, `PUT /competitions/{id}/tables/{tableId}` | `{ competitionId, tableId, addedJudgeIds: [Guid], removedJudgeIds: [Guid] }` | Organization: recomputes `NotValidForBos` for entries owned by the added and removed judges against current engagement (FR-018) |
| `judging.evaluation-corrected` | Judging: `PUT /competitions/{id}/evaluations/{evaluationId}` on a `Finalized` competition | `{ competitionId, beerEntryId, evaluationId }` | Publication: stages `RefreshResults` (regenerate the entry PDF, rebuild the ZIP, no emails; FR-083) |

## Delivery

- Each delivery is retried with exponential backoff (1 s, doubling, max 60 s) up to 5 attempts by
  default. After the last failure the event is marked `Dead`. An error is logged with type, id and
  failing subscribers, and never the payload. Settings come from the `Messaging` section.
- An unknown `type`/`version` goes to `Dead` straight away.
- Retention (FR-090): delivered events purged after 7 d, dead after 30 d, inbox records after 14 d (defaults). The inbox retention always outlasts any redelivery, so de-duplication holds.
- Expected delay from commit to reaction: ≤ 1 s (FR-089). The dispatcher is woken after commit,
  and a safety-net poll (default 30 s) covers missed signals and restarts.
