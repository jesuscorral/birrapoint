# 0012 - Public welcome landing page, Keycloak `check-sso` instead of `login-required`

**Status:** Accepted
**Date:** 2026-08-02

## Context

FR-001 originally redirected every unauthenticated visitor to Keycloak before anything rendered
(`onLoad: 'login-required'`). The design pass added a public landing (`WelcomeComponent`) with
branding and explicit "Iniciar sesión" / "Crear cuenta de organizador" actions, which cannot render
under `login-required`. The switch to `check-sso` was first made in code without a spec change;
this ADR and the FR-001 amendment formalize it.

## Decision

1. `onLoad: 'check-sso'`: detect an existing session at bootstrap without forcing a redirect.
2. Public routes are only the landing (`/`) and `/auth/handoff` (which calls
   `keycloak.login()`/`register()` — credentials are still entered only on Keycloak). Every other
   route keeps its role guard.
3. Authenticated users never see the landing: `homeRedirectGuard` sends them to their workspace.
4. PKCE `S256` is unchanged.

## Consequences

- Visitors can see what BirraPoint is before logging in.
- There is no implicit authentication safety net any more: **every new route must declare a
  guard**, and reviewers must check it.
