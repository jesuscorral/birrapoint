---
name: "offline-sync"
description: "Audit the frontend offline engine for correctness against R-08: Dexie drafts/outbox pattern, debounce timing, replay triggers, idempotency, and IndexedDB-never-source-of-truth."
user-invocable: true
disable-model-invocation: false
---

# Offline Sync Audit

BirraPoint's judge evaluation flow must work fully offline (R-08, CLAUDE.md §Frontend
conventions). The pattern is fixed: Dexie stores `drafts` (autosaved local state) and `outbox`
(submitted-but-unsynced), replayed on reconnect. Deviating from this pattern silently breaks the
offline guarantee in ways that only show up in the field (flaky connectivity, iOS Safari), not
in a normal dev session — that's why this needs an explicit audit rather than relying on "it
worked when I tested it online."

## Steps

1. **No bypass.** Grep judge-facing frontend flows (`features/evaluation-sheet/`,
   `features/judge-tables/`) for direct `HttpClient`/`fetch` calls that write data without going
   through the offline sync layer in `core/offline/`. Any judge-facing write must go
   draft → outbox → sync, never straight to the API.
2. **Draft debounce.** Confirm draft persistence to the Dexie `drafts` store happens ≤300 ms
   after each change (performance budget, Principle IX) and is debounced, not fired on every
   keystroke uncoordinated.
3. **Outbox correctness.** Confirm items only move from `drafts` to `outbox` on explicit submit,
   and that the outbox entry carries the deterministic idempotency key
   `{competitionId}:{tableId}:{judgeId}:{entryId}` (FR-029/R-07) so replay is safe.
4. **Replay triggers.** Confirm `SyncService` replays the outbox on exactly these three triggers
   — `window` `online` event, app start, and immediately after each new submit — and nothing
   else. Flag any use of the Background Sync API (unsupported on iOS Safari, explicitly banned
   by R-08).
5. **Replay semantics.** Confirm a replayed submit that gets a `200` (idempotent replay,
   already-stored evaluation) is treated as success and removed from the outbox — not retried
   forever, not treated as an error.
6. **IndexedDB is not truth.** Confirm the UI reconciles against server state after sync/reconnect
   rather than trusting the local Dexie copy indefinitely — especially after a `TableClosed` or
   `JudgeRemoved` SignalR event, which must reject/surface stale outbox items rather than
   silently keep retrying them (FR-034).
7. **Storage failure handling.** Confirm there's an explicit, visible warning when local storage
   is unavailable or full (spec edge case) — this must never fail silently, since a judge who
   thinks their score saved but it didn't is a data-loss incident.

## Report format

Table: `file:line | pattern checked | issue | severity (blocker/warning) | fix`.

Blockers = any write path bypassing the offline layer, any use of Background Sync API, missing
idempotency key, or silent storage-failure handling. Everything else (debounce slightly off,
naming) is a warning.

Do not fix blockers automatically — report first, fix only after confirmation.
