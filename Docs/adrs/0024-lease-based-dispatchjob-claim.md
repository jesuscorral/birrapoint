# 0024 - Lease-based atomic claim for `DispatchJob`

**Status:** Accepted
**Date:** 2026-10-10

## Context

R-06 assumed a single `DispatchWorker`. Rollouts break that: ACA (single revision mode) and ECS
briefly run the old and new API revision together. The worker read all `Pending` jobs without a
claim and reset every `Running` job at startup, so two workers could run one job (duplicate result
emails, double PDFs) and a new revision could reset a job the old one was still executing
(PR #46 review M3). SignalR still has no backplane, so the API stays at one replica.

## Decision

- Add `DispatchJob.LeaseOwner` and `LeaseExpiresAt` (migration `AddDispatchJobLease`).
- Claim one eligible `Pending` job per short transaction with `FOR UPDATE SKIP LOCKED`, set
  `Running` and the lease, then run the handler outside the transaction.
- Renew the lease every `LeaseDuration`/3 (`Dispatch:LeaseDuration`, default 2 min, must be > 0).
- Write the outcome only `WHERE LeaseOwner = me`. On a lost lease, discard the result and log.
- Recover `Running` jobs with an expired lease at startup and every sweep, through the existing
  retry/backoff path. Live leases are never touched. A `Running` job with no lease was claimed by
  a pre-lease revision and counts as orphaned only once its `UpdatedAt` is older than
  `LeaseDuration` (PR #65 review M1), so the first rollout does not re-run it immediately.
- `RetryDispatch` resets only `Failed` `SendResultEmail` jobs (FR-041).

Rejected: a broker or Hangfire (extra dependency, R-06 still holds), Postgres advisory locks
(a lock held for the whole handler pins a connection and hides crashes less clearly than a lease).

## Consequences

- Overlapping revisions cannot double-process a job. A job orphaned by a stopped revision waits up
  to `LeaseDuration` before recovery.
- Handlers still must be idempotent: a handler outliving a lost lease may finish its side effects
  while another worker retries.
- **First rollout of this change is not covered**: the old revision still claims without a
  lease and keeps its in-memory batch, so deploy it while no dispatch is in flight (no
  competition being finalized). Later rollouts are safe.
- Two new nullable columns and one index; `data-model.md` and R-06 updated.
- Raising the API replica count still needs a SignalR backplane (ADR-0016 constraint on SignalR
  remains; the job-queue half is lifted).
