---
name: "offline-sync"
description: "Dexie-based offline-first pattern (drafts + outbox, replay triggers, idempotent submit) for judge-facing flows (R-08/FR-027/FR-029). Load before touching evaluation drafts, the outbox, or any judge write path."
user-invocable: true
disable-model-invocation: false
---

# Offline-first sync (R-08)

Two Dexie stores, both caches — **never** the source of truth (`data-model.md` §Client-side
stores):

- `drafts` (key `beerEntryId`): in-progress sheet fields, persisted ≤300 ms after each change
  (SC-003), deleted on successful submit.
- `outbox` (key `idempotencyKey`): submitted-but-unsynced evaluation payloads + attempt metadata,
  replayed until acknowledged.

## Idempotency key

Fixed format (R-07), deterministic — recomputed the same way on every replay, never regenerated
per attempt:

```text
X-Idempotency-Key: {competitionId}:{tableId}:{judgeId}:{entryId}
```

## Replay triggers — exactly three, no others

1. `window` `online` event
2. app start
3. immediately after each submit

Do **not** use the Background Sync API — unsupported on iOS Safari, which judges use.

## Server contract

Replay of an already-stored evaluation returns `200` with the stored result (not `201`, not a
duplicate row, never an UPSERT). The server is the arbiter of "already submitted," not the client.

## Write path discipline

Judge-facing writes never go direct through `HttpClient` — they go through the sync/outbox
service. A direct `HttpClient.post` from a judge feature component that bypasses the outbox is a
bug, not a shortcut: it breaks the offline guarantee and the exactly-once contract.

## Failure mode to handle explicitly

Local storage unavailable/full must surface a UI warning (spec edge case) — never fail a draft
save silently.

## Locking interaction

Once a sheet is submitted it's locked server-side; a late offline sync arriving after table close
is rejected (`409 table-closed` per FR-034), and the frontend must surface that rejection to the
judge rather than swallowing it.
