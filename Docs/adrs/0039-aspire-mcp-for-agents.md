# 0039 - Aspire MCP for coding agents

**Status:** Accepted
**Date:** 2026-11-07

## Context
Agents diagnosed the local stack with `docker logs`, `curl` and the read-only `postgres-local` MCP. This is slow and fragile: container names are dynamic and there is no view of structured logs or traces. The Aspire CLI (13.6.x) ships `aspire agent mcp`, an MCP server that talks to running AppHosts through the local backchannel. The AppHost SDK also lagged the hosting packages (13.5.4 vs 13.6.1), so versions were aligned first (13.6.4; MailPit toolkit 13.6.1).

## Decision
- Register `aspire` in `.mcp.json` as `aspire agent mcp --nologo --non-interactive` (stdio, no `--dashboard-url`). It only reaches AppHosts running on this machine, never a cloud environment.
- Allow the read tools in `.claude/settings.json` (resources, console and structured logs, traces, `doctor`, docs). Put `execute_resource_command` (start/stop/restart) in `ask`.
- `backend-`, `frontend-` and `qa-engineer` get the read-only diagnostic tools only. Resource commands stay in the main session, with a prompt.
- `/stack-status` calls `list_resources` first and falls back to docker/curl.

## Consequences
- Faster, structured diagnosis (status, health, logs, traces) without guessing container names.
- Needs Aspire CLI >= 13.6.4 locally; without it agents use the docker/curl fallback.
- Alternatives rejected:
  - Docker logs/curl only: no traces, no health model, brittle names.
  - `aspire agent init`: also installs skills and rewrites agent config outside our review.
  - `execute_resource_command` for all agents: a subagent could restart or stop shared resources (Keycloak, Postgres) and lose local state.
- `ASPIRE010` is suppressed in the AppHost csproj because DCP and the dashboard come from SDK NuGet packages, not the CLI bundle. Revisit when the CLI bundle is the supported path.
