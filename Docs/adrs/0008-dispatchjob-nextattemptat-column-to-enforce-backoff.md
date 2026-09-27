# 0008 - `DispatchJob.NextAttemptAt` column to actually enforce retry backoff

**Status:** Accepted
**Date:** 2026-07-17

## Context

`DispatchRetryPolicy` computed a capped exponential backoff, but the worker picked up every
`Pending` job on any wake-up (new enqueue, 30 s poll, another job's retry), so failed jobs were
retried immediately and kept hammering SMTP/Keycloak (PR #9 review). Deriving eligibility from
`Attempts` at query time would break if the curve changed.

## Decision

Add nullable `DispatchJob.NextAttemptAt`. A failure that returns the job to `Pending` sets it to
`now + BackoffDelay(Attempts)`; the dispatch sweep selects
`Status = Pending AND (NextAttemptAt IS NULL OR NextAttemptAt <= now)`. Index
`(Status, NextAttemptAt)`. The channel wake-up is only an optimization.

## Consequences

- Backoff is enforced by the query every job must pass.
- One nullable column and one index; `data-model.md` amended. `DispatchJob` is never exposed over
  REST or SignalR, so contracts are unchanged.
- Any future reader of `DispatchJob` must respect `NextAttemptAt`, not only `Status`.
