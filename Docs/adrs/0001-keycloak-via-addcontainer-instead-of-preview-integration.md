# 0001 - Keycloak orchestrated via AddContainer instead of the preview Aspire integration

**Status:** Accepted
**Date:** 2026-07-07

## Context

Task T005 requires orchestrating Keycloak 25+ in the .NET Aspire AppHost with automatic import
of the `birrapoint` realm (FR-044, R-16). Aspire offers a first-class integration
(`Aspire.Hosting.Keycloak`, providing `AddKeycloak(...)` + `WithRealmImport(...)` and a built-in
health check), but on the version train in use (13.4.x) that package **only exists as a
preview** (`13.4.6-preview.1`), while every other hosting package we use (PostgreSQL, MailPit)
is stable. Constitution Principle V requires every dependency to be justified and penalizes
unnecessary risk; mixing a preview package with the stable SDK can also drag in version
conflicts.

## Decision

Orchestrate Keycloak with the stable generic API `builder.AddContainer("keycloak",
"quay.io/keycloak/keycloak", "26.2")`, wiring manually: `start-dev --import-realm`, bootstrap
credentials via environment variables (local-dev placeholders only), a `WithBindMount` of
`infra/keycloak/` onto `/opt/keycloak/data/import`, and a proxied HTTP endpoint on port 8081.
`Aspire.Hosting.Keycloak` is not adopted until it ships a stable release on the train in use.

## Consequences

- **Positive**: zero preview dependencies; the AppHost uses stable packages only; the container
  behaves identically to production (same image and flags as the future ACA deployment).
- **Negative**: no built-in health check, so the AppHost cannot `WaitFor` a *ready* Keycloak —
  a latent cold-start race once the API depends on Keycloak (T011+). Planned mitigation: add a
  manual HTTP health check (`WithHttpHealthCheck` against `/realms/birrapoint`) or migrate to
  `AddKeycloak` once stable.
- **Review trigger**: re-evaluate on every Aspire train update; if `Aspire.Hosting.Keycloak`
  stabilizes, migrate and mark this ADR as Superseded. (Also flagged by the automated review of
  PR #2.)
