# 0001 - Keycloak orchestrated via AddContainer instead of the preview Aspire integration

**Status:** Accepted
**Date:** 2026-07-07

## Context

The local AppHost must run Keycloak with the `birrapoint` realm auto-imported (FR-044).
`Aspire.Hosting.Keycloak` (`AddKeycloak` + `WithRealmImport` + health check) exists only as a
preview package on the Aspire train in use (13.4.x), while every other hosting package is stable.
Principle V penalizes unjustified dependency risk.

## Decision

Use the stable generic API: `AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.2")` with
`start-dev --import-realm`, local-dev bootstrap credentials as environment variables, a read-only
bind mount of `infra/keycloak/` onto `/opt/keycloak/data/import`, and an HTTP endpoint on port
8081. Adopt `Aspire.Hosting.Keycloak` only once it ships stable.

## Consequences

- No preview dependencies in the AppHost.
- No built-in health check: the AppHost cannot `WaitFor` a *ready* Keycloak, so the API can start
  before Keycloak answers. Mitigation if it becomes a problem: `WithHttpHealthCheck` against
  `/realms/birrapoint`.
- Re-evaluate on each Aspire update; migrate and mark this ADR Superseded when the integration is
  stable.
