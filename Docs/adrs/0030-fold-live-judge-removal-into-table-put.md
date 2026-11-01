# 0030 - Fold live judge removal into table PUT; remove the DELETE endpoint

**Status:** Accepted
**Date:** 2026-10-31

## Context
Table membership could be changed by two endpoints: `PUT /competitions/{id}/tables/{tableId}` (full desired state, `Draft`/`Active`) and `DELETE /competitions/{id}/tables/{tableId}/judges/{judgeId}` (live removal, `InEvaluation`, FR-039). Each had its own state gate, validation and audit path, so rules about membership lived in two places and the Tables slice mixed unrelated handlers.

## Decision
Remove the `DELETE` endpoint. `PUT` is the single mutation surface for table membership:

| Competition state | `PUT` behaviour |
|---|---|
| `Draft` / `Active` | Unchanged: full replacement, COI and BOS rules. |
| `InEvaluation` | Accepted only as a pure judge removal: `name` and `beerEntryIds` equal the current ones, `judgeIds` is a subset of the current active judges (`LiveEditRules`). Otherwise `409 invalid-state-transition`. |
| `Finalized` | `409 invalid-state-transition`. |

A pure removal (`LiveJudgeRemoval`) runs under a `SELECT ... FOR UPDATE` row lock on the table: soft-removes each omitted judge (`RemovedAt`), audits `JudgeRemoved`, re-reconciles open discrepancies, and publishes `JudgeRemoved` and `DiscrepancyResolved` after commit. An identical body is a `200` no-op; a closed table is `409 table-closed`. The Tables slice is split into per-use-case folders (`CreateTable/`, `ListTables/`, `UpdateTable/`).

The frontend `TablesApiService.removeJudge` builds the PUT body from the table's current name, judges and samples.

## Consequences
- One place for membership rules, state gates and concurrency; fewer endpoints to secure and test.
- Breaking contract change: `DELETE .../judges/{judgeId}` is gone (`rest-api.md` updated). No external clients exist, so no versioning was needed.
- Trade-off: the client must send the full table state to remove one judge. A stale client view (e.g. a sample moved meanwhile) gets `409` instead of silently removing; it must re-fetch and retry.
- Trade-off: the `InEvaluation` subset rule is an extra branch in `PUT` that must stay in sync with the live-event invariants (no sample or name edits once evaluation starts).
- FR-039 in `spec.md` is endpoint-agnostic and unchanged.
