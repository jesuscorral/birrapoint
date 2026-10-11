# 0028 - Health endpoints in all environments

**Status:** Accepted
**Date:** 2026-10-11

## Context
`/health` and `/alive` were mapped in Development only, so Azure Container Apps (ACA) used the default TCP probe and nothing could tell a started-but-broken API from a healthy one (FR-048, T098). Probing the database from the cloud is costly: ADR-0025 showed that a periodic DB check keeps the Neon compute awake and burns the free compute budget. OpenTelemetry also reported `unknown_service` in ACA/ECS because no service name was set.

## Decision
- `MapDefaultEndpoints` (ServiceDefaults) maps both endpoints anonymously in every environment, returning bare status text (`Healthy`/`Degraded`/`Unhealthy`), `200` or `503`, no details:
  - `/alive`: only checks tagged `live` (process is up, no DB).
  - `/health`: all checks, including the new `db` check (`DatabaseHealthCheck`: `CanConnectAsync`, 4 s timeout, fixed descriptions, no exception text).
- Cloud probes use `/alive` only. `/health` is for manual and local use because it would keep Neon awake.
- ACA API gets startup, readiness and liveness probes on `/alive:8080` replacing the default TCP probe. Startup allows 25 s x 10 = 250 s (ACA caps the threshold at 10), covering migrations plus Dapr secret retries (~200 s). A revision that does not answer `/alive` stays unready, so the `wait-revisions` deploy gate now fails it. The first rollout needs an image that contains `/alive`.
- AWS API has no probe: it has no ALB target group (reached through Cloud Map) and the aspnet image has no `curl`. `ecs-health.sh` is unchanged.
- The API ingress is internal on Azure (`external_enabled = false`) and not routed by the ALB on AWS. Only `/api/` and `/hubs/` are proxied by the web nginx, so the endpoints are not public.
- The OTel service name is set explicitly (`OTEL_SERVICE_NAME`, else the application name).

## Consequences
- Positive: ACA detects an API that started but cannot serve; the same endpoints work in every environment; no DB traffic from probes.
- Negative: `/alive` does not prove DB reachability, so a Neon outage is not seen by probes. AWS gets no API liveness signal beyond ECS task state.
- Cloud telemetry stays console logs (Log Analytics on ACA, CloudWatch on ECS). OTLP export only activates when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and no cloud sets it. Validation in ACA/ECS is deferred to T099.
- Follow-up (T099): the Keycloak ACA startup probe uses `failure_count_threshold = 30`, above the documented maximum of 10.
