---
name: "realtime-hub-events"
description: "CompetitionHub group/guard/emit conventions — server-to-client-only SignalR notifications that clients treat as a re-fetch signal, not as the source of truth. Load before adding or consuming a hub event."
user-invocable: true
disable-model-invocation: false
---

# CompetitionHub conventions

Single hub: `CompetitionHub`. Direction is strictly server → client; every mutation is still a
REST call. The hub only notifies.

## Groups — both membership-guarded at join time, not just at emit time

- `competition:{id}:organizers` — join requires organizer role AND ownership of that competition.
- `table:{tableId}` — join requires active membership on that table (a removed judge, FR-034/US12,
  must be ejected, not just stop receiving future emits).

## Emit-after-commit, always

Events are published from `Realtime/EventPublisher.cs` only after the owning transaction's
`SaveChangesAsync` succeeds. Never emit inside a handler before the commit — a rolled-back
transaction must never have fired a notification.

## Reconnect contract

Clients re-join their groups and re-fetch current state on reconnect. A missed event while
disconnected is not a bug to fix by replaying event history — the frontend's job is to treat
reconnect as "state may be stale, refetch," per `core/realtime/` connection service (T020).

## Event catalog

Extend this list, don't rename existing entries without a contract amendment to
`signalr-hub.md`:

| Event | Story |
|---|---|
| `CompetitionStateChanged` | US2 |
| `TableOrderFixed` | US6 |
| `EvaluationCompleted` | US7 |
| `TableClosed` | US8 |
| `DiscrepancyRaised` / `DiscrepancyResolved` | US11 |
| `DispatchProgress` | US10 (organizer group only) |
| `JudgeRemoved` | US12 |

## Anonymity

Judge-facing emits must obey `blind-tasting-integrity` — a `table:{tableId}` payload is exactly as
bound by BR-01 as a REST judge DTO.
