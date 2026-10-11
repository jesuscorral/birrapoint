# 0026 - Production Keycloak hardening

**Status:** Accepted
**Date:** 2026-10-11

## Context
The production realm was the dev realm minus the seeded account and local secrets. It had no brute-force protection or password policy, self-registration granted `ORGANIZER` without email verification, the admin console was reachable on the public hostname, and a missing `API_ADMIN_CLIENT_SECRET` silently produced a Keycloak or API with a dev/blank admin secret (PR #46 review M4). The dev realm must stay unchanged: Aspire and the E2E suite rely on it.

## Decision
- Hardening lives in `infra/keycloak/realm-prod.jq`, a prod-only derivation applied at image build. `RealmProd.test.sh` asserts the result.
- **Brute force** on, temporary lockout only: 5 failures, 60 s wait doubling to 15 min, `permanentLockout=false`. Permanent lockout would let anyone lock organizers out.
- **Password policy** `length(12) and notUsername and notEmail`. Limited to rules the judge temporary-password generator (24 random base64 chars, `KeycloakAdminClient.cs`) always satisfies. Character-class rules require changing the generator first.
- **`verifyEmail=true`**, because self-registration grants `ORGANIZER`. It depends on SMTP, which is a required Terraform variable in both roots.
- **Admin console off**: `KC_FEATURES_DISABLED=admin` (feature name verified on Keycloak 26.2.5). The `admin-api` feature stays on for judge provisioning.
- **Fail fast**: the Keycloak entrypoint exits when `API_ADMIN_CLIENT_SECRET` is missing (`ApiAdminSecretGuard.test.sh`). The API rejects a missing, blank or dev-placeholder `Keycloak:AdminClientSecret` outside Development/Testing (`KeycloakAdminOptionsValidator`, `ValidateOnStart`).
- The temporary bootstrap admin is replaced through the Admin REST API or `kcadm` (`infra/keycloak/admin-runbook.md`).

## Consequences
- Brute-force and weak-password exposure drops without a new dependency or a dev-realm change.
- Organizer sign-up in production now needs working SMTP; a broken mailer blocks new organizers.
- With the console off, realm administration is only via REST/`kcadm`, which is slower for ad-hoc changes.
- Real misconfiguration (unset or placeholder secret) fails at startup instead of at the first judge provisioning.
- Realm import runs on first start only. Existing environments must get the hardening applied through REST or be wiped and redeployed.
- The `master` realm is not covered by the realm import (it already exists at first start), so its brute-force protection (same thresholds, temporary lockout) is a mandatory runbook step done with the permanent admin, not automated at image start: a post-start `kcadm` hook would race the server boot and need the bootstrap credentials in the entrypoint. Until an operator runs it, the public `admin-cli` password grant on `master` has no lockout. The permanent admin password must be long and random.
- A stronger password policy (classes, history) is blocked until the temporary-password generator is changed.
