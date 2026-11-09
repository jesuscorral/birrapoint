# Data Model: Integration events between modules

The binding definitions live in the living contracts, amended by this feature:

- Tables `OutboxMessages` / `InboxMessages` and the schema map: [baseline data-model.md](../001-birrapoint-mvp/data-model.md) (§Schemas, §OutboxMessage / InboxMessage). The new `DispatchJob.Type` value `RefreshResults` is defined there too.
- Event types, payloads and delivery rules: [contracts/integration-events.md](../001-birrapoint-mvp/contracts/integration-events.md).
- The new realtime event `JudgeConflictDetected`: [contracts/signalr-hub.md](../001-birrapoint-mvp/contracts/signalr-hub.md).
- The `PUT /competitions/{id}/judges/{judgeId}` behaviour: [contracts/rest-api.md](../001-birrapoint-mvp/contracts/rest-api.md).

## Tables added by this feature's migration

| Schema | Table | Owner |
|---|---|---|
| `organization` | `OutboxMessages`, `InboxMessages` | Organization (publishes `competition-finalized`, `judge-email-changed`; consumes `table-judges-changed`) |
| `judging` | `OutboxMessages`, `InboxMessages` | Judging (publishes `table-judges-changed`, `evaluation-corrected`; consumes `judge-email-changed`) |
| `publication` | `InboxMessages` | Publication (consumes `competition-finalized`, `evaluation-corrected`) |

No existing table or column changes. One migration, `AddIntegrationMessaging`, creates only these
five tables and their indexes; `Down` drops them.

## Outbox state machine

```text
Pending ──claim──▶ Delivering ──all subscribers ok──▶ Delivered
   ▲                   │
   └──retry (backoff)──┤ failure, attempts < max
                       └──failure, attempts = max, or unknown type──▶ Dead
Delivering with expired lease ──recovered by any dispatcher──▶ Delivering (re-claimed)
```

## Payloads (v1)

| Type | Payload |
|---|---|
| `organization.competition-finalized` | `{ competitionId }` |
| `organization.judge-email-changed` | `{ competitionId, judgeId }` |
| `judging.table-judges-changed` | `{ competitionId, tableId, addedJudgeIds, removedJudgeIds }` |
| `judging.evaluation-corrected` | `{ competitionId, beerEntryId, evaluationId }` |
