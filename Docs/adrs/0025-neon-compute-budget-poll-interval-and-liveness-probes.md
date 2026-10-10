# 0025 - Neon compute budget: configurable dispatch poll and liveness-only probes

**Status:** Accepted
**Date:** 2026-10-10

## Context

Neon suspends idle compute; the Free plan allows 100 CU-h per project per month and suspends
compute when exceeded (PR #46 review M7). Two things kept it awake 24/7: the `DispatchWorker`
30 s safety-net poll and the Keycloak/ALB health checks on `/health/ready`, which validate pooled
DB connections (verified: `pg_stat_activity.state_change` moves on every `/health/ready` call, not
on `/health/live`). At 0.25 CU, 24/7 is about 182 CU-h/month.

## Decision

- `Dispatch:SafetyNetPollInterval` (default 30 s, floor 1 s, validated on start). Terraform
  `dispatch_poll_interval` defaults to `01:00:00` on both clouds.
- `RetryDispatch` wakes the worker immediately, so a manual retry no longer depends on the poll.
- Keycloak readiness probe (Azure) and ALB target-group health check (AWS) use `/health/live`.
- `neon_project.primary_compute.autoscaling_limit_min_cu = 0.25` (in-place update).
- Document the budget in both Terraform READMEs and runbooks; the Launch plan is a prerequisite
  for live events.

Rejected: keeping the 30 s poll on a paid plan only (wastes compute for no benefit); removing the
poll (loses crash and cross-revision recovery).

## Consequences

- Idle estimate is about 60-80 CU-h/month, inside the Free plan; traffic during events adds to it.
- With a 1 h poll, backed-off retries after a restart, `Pending` jobs from another revision and
  expired-lease recovery after a crash wait up to 1 h (startup sweeps once immediately).
- Probes no longer detect an unreachable DB; a DB outage shows as request errors instead of
  restarts or unhealthy targets.
- Estimates are unverified on real accounts; check Neon console CU-hours in T099/T137.
