# 0008 - `DispatchJob.NextAttemptAt` column to actually enforce retry backoff

**Status:** Accepted
**Date:** 2026-07-17

## Context

T016's initial implementation computed a capped exponential backoff (`DispatchRetryPolicy`,
unit-tested in isolation) but did not enforce it: `DispatchWorker`'s dispatch sweep selected every
`Status == Pending` row unconditionally. A failed job set back to `Pending` was therefore
re-attempted on the very next wake — a new unrelated `EnqueueAsync` call, the 30-second
safety-net poll, or even another job's own delayed retry signal — rather than actually waiting out
its computed delay. senior-code-reviewer caught this on PR #9 (T016) as a high-severity finding:
the whole point of backoff is to stop hammering a failing downstream dependency (SMTP, Keycloak —
once T041/T075 land), and a policy that's computed but not consulted by the query doesn't do that.

`data-model.md`'s original `DispatchJob` shape (Id, CompetitionId, Type, PayloadJson, Status,
Attempts, LastError) has no field to express "not eligible again until X" — enforcing the delay
requires either persisting that instant, or reconstructing it from `Attempts` + a fixed formula at
query time (fragile: it silently breaks if `DispatchRetryPolicy`'s curve ever changes, since old
rows would be reinterpreted under the new curve).

## Decision

Add `DispatchJob.NextAttemptAt` (`DateTimeOffset?`, null by default). `RecordFailureAsync` sets it
to `UtcNow + DispatchRetryPolicy.BackoffDelay(Attempts)` whenever a job is set back to `Pending`;
`DispatchWorker`'s dispatch sweep filters on `Status == Pending && (NextAttemptAt == null ||
NextAttemptAt <= now)`. A composite index on `(Status, NextAttemptAt)` supports both this query
and the startup resume-sweep (`Status == Running`). The channel-based wake-up signal
(`ScheduleRetrySignal`) becomes an optimization only — an early wake that still has to pass the
`NextAttemptAt` filter to actually be picked up — not the mechanism enforcing the delay.

## Consequences

- Backoff is now enforced by the query a failed job actually has to pass, not merely by how soon
  something happens to wake the worker — the property `DispatchRetryPolicyTests` verifies
  (`BackoffDelay` growing per attempt) now corresponds to real dispatch-eligibility behavior.
- One more nullable column and one more index on `DispatchJob`; negligible cost at MVP volume.
- `data-model.md`'s `DispatchJob` table is amended to include this field (Principle X — schema
  changes travel with their implementation, not silently). This is a data-model correction
  discovered during implementation and review, not a new requirement — no `spec.md`/contracts
  change, since `DispatchJob` is never exposed directly over REST or SignalR (only the derived
  `DispatchProgress` event payload is, and its shape is unchanged).
- Any future job-processing surface that reads `DispatchJob` directly (none exist yet) must
  respect `NextAttemptAt`, not just `Status`, when deciding what's eligible to run.
