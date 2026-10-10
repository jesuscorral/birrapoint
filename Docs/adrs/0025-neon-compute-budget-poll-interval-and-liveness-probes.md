# 0025 - Neon compute budget: configurable dispatch poll and liveness-only probes

**Status:** Accepted
**Date:** 2026-10-10

## Context

Neon suspends idle compute; the Free plan allows 100 CU-h per project per month and suspends
compute when exceeded (PR #46 review M7). Two things kept it awake 24/7: the `DispatchWorker`
30 s safety-net poll and the Keycloak/ALB health checks on `/health/ready`, which validate pooled
DB connections (verified: `pg_stat_activity.state_change` moves on every `/health/ready` call, not
on `/health/live`). At 0.25 CU, 24/7 is about 182 CU-h/month.

PR #67 review M2 found two further Keycloak wake sources, measured on Keycloak 26.2 + Postgres 16
(idle, `pg_stat_activity` sampled every 30 s plus `log_statement=all`, 37 min): the default `ispn`
cache runs JDBC_PING and polls `JGROUPS_PING` every 30-40 s (longest quiet gap 65 s), and the
Quarkus Agroal pool validates idle connections every 2 min (`state_change` moves with no
statement logged). Either keeps a 5 min suspend timer from ever firing. The 15 min housekeeping task
(`ClearExpired*`, ~1 s of queries) was the only source the first estimate counted.

## Decision

- `Dispatch:SafetyNetPollInterval` (default 30 s, floor 1 s, validated on start). Terraform
  `dispatch_poll_interval` defaults to `01:00:00` on both clouds.
- `RetryDispatch` wakes the worker immediately, so a manual retry no longer depends on the poll.
- Each sweep (startup included) ends by scheduling one wake at the earliest `Running`
  `LeaseExpiresAt` or `Pending` `NextAttemptAt` (+1 s), capped at the poll interval (PR #67 review
  M1). A startup sweep alone is not enough: a crashed worker's lease is still live then. Nothing
  pending means no wake and no DB activity.
- Keycloak readiness probe (Azure) and ALB target-group health check (AWS) use `/health/live`.
- `neon_project.primary_compute.autoscaling_limit_min_cu = 0.25` (in-place update).
- Keycloak image (`infra/keycloak`): `KC_CACHE=local` (build and run stage), and
  `conf/quarkus.properties` with `quarkus.datasource.jdbc.background-validation-interval=0` and
  `foreground-validation-interval=5S`. Without foreground validation a connection closed by Neon on
  suspend fails the first request (measured: HTTP 500 twice); with it the request succeeds.
- Terraform sets `KC_SPI_SCHEDULED_INTERVAL=21600` (seconds, Azure and AWS): housekeeping every 6 h
  instead of 15 min. The key is read from Keycloak's `scheduled` config scope; it works in 26.2
  (debug log "with interval 21600000 ms") but is absent from `kc.sh start --help-all`.
- Document the budget in both Terraform READMEs and runbooks; the Launch plan is a prerequisite
  for live events.

Rejected: keeping the 30 s poll on a paid plan only (wastes compute for no benefit); removing the
poll (loses crash and cross-revision recovery).

## Consequences

- Idle estimate is about 20 CU-h/month (about 3 from Keycloak housekeeping, up to about 16 from
  the hourly poll, at about 5.5 min per wake), inside the Free plan; traffic during events adds to
  it. With the Keycloak image defaults it is about 182 CU-h/month (never suspends), over the cap.
- Keycloak runs one replica (`KC_CACHE=local`, no clustering); scaling out needs `ispn` again,
  which implies a paid Neon plan. AWS deploys stop the old task before starting the new one
  (`deployment_minimum_healthy_percent = 0`, maximum 100), so there is a short Keycloak outage.
  Azure Container Apps single-revision mode has no setting to avoid overlap: the old and new
  revisions run together until the new one is ready, so an in-progress login or an Admin-API
  password reset can be seen by the other node (separate caches). Deploy outside live events. The `quarkus.properties` and `KC_SPI_SCHEDULED_*`
  settings are not in `--help-all`; re-test after a Keycloak upgrade.
- Neon-side behaviour (what counts as activity, per-wake billing) is still not measured; check
  console CU-hours in T099/T137.
- With a 1 h poll, only `Pending` jobs written by another revision (not seen by this worker's
  sweep) and legacy NULL-lease `Running` rows (pre-T129) wait up to 1 h; lease expiry and backed-off retries are covered by the scheduled wake, and a failed sweep retries after 5 s * 2^n (capped at the poll interval) instead of waiting for the poll.
- Probes no longer detect an unreachable DB; a DB outage shows as request errors instead of
  restarts or unhealthy targets.
