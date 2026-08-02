# 0012 - Public welcome landing page, Keycloak `check-sso` instead of `login-required`

**Status:** Accepted
**Date:** 2026-08-02

## Context

FR-001 originally required redirecting every unauthenticated visitor straight to the Keycloak
login portal before any content rendered (`keycloakInitOptions.onLoad: 'login-required'`). The
"Botella y cobre" design pass added a public `/welcome` landing page (`WelcomeComponent`) —
branding, feature highlights, and two explicit calls to action ("Iniciar sesión" /
"Crear cuenta de organizador") — so a first-time visitor can see what BirraPoint is before being
thrown at a bare Keycloak login form.

`onLoad: 'login-required'` is incompatible with that: Keycloak forces the redirect before
Angular ever bootstraps a route, so `/welcome` could never render for an unauthenticated caller.
This was switched to `onLoad: 'check-sso'` (silently detects an existing session without forcing
one) directly in `keycloak.providers.ts`, explained only by an inline code comment, with no spec
amendment and no ADR — `keycloak.providers.spec.ts`'s test asserting `'login-required'` was left
failing on `main`. A retroactive code review (see PR remediation for `47a77f4`/`803c18e`) flagged
this as a requirement silently overridden in code, which this ADR and the accompanying FR-001
amendment (spec.md, Session 2026-08-02) resolve.

## Decision

1. `keycloakInitOptions.onLoad` is `'check-sso'`, not `'login-required'`. On app bootstrap this
   checks for an existing Keycloak session without forcing a redirect; an unauthenticated visitor
   simply sees whatever route they landed on with no session.
2. The public route surface for unauthenticated users is deliberately small: `/welcome` (landing)
   and `/auth/handoff` (the actual `keycloak.login()`/`keycloak.register()` call, still 100%
   Keycloak-hosted credential entry — no custom login form, no relaxation of Principle VII).
   Every other route stays behind the existing role guards; `check-sso` only changes *when* the
   redirect can be deferred to, not which routes require auth.
3. Authenticated users must never see `/welcome` — `homeRedirectGuard` routes them straight to
   their role's workspace (organizer dashboard / judge tables) the moment a valid session is
   detected, so `check-sso` never regresses FR-002's role-based landing.
4. `pkceMethod: 'S256'` is unchanged (still required client-side per R-11, independent of
   `onLoad`).

## Consequences

- FR-001 (spec.md) is amended to describe this behavior instead of the old unconditional
  redirect; `keycloak.providers.spec.ts` now asserts `'check-sso'`.
- Unauthenticated users can now see product marketing content before any login prompt, which is
  the actual product goal of the "Botella y cobre" landing — this was the point of the change,
  just previously undocumented.
- Any future route added outside `/welcome`/`/auth/handoff` must still declare an explicit role
  guard; `check-sso` provides no session enforcement of its own, it only stops forcing the
  redirect at bootstrap. A route added without a guard would now be silently reachable
  unauthenticated, where under the old `login-required` config it would have been caught for
  free — reviewers should treat "did this new route get a guard" as the thing to check now,
  since the previous implicit safety net is gone.
