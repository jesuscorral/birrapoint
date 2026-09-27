# 0009 - JWT audience validation for the API

**Status:** Accepted
**Date:** 2026-07-17

## Context

JWT validation initially checked only issuer and signature (`ValidateAudience = false`), so a token
issued for any client of the realm would be accepted. This had to close before the first protected
endpoint shipped.

## Decision

- An `oidc-audience-mapper` on the `birrapoint-spa` client (`infra/keycloak/birrapoint-realm.json`)
  stamps `birrapoint-api` into every access token. It is attached to the client, not to a new
  default client scope, so the implicit `roles` scope is not dropped.
- The API sets `ValidateAudience = true` with `ValidAudience` from `Keycloak:ApiAudience`
  (AppHost locally, Terraform `Keycloak__ApiAudience` in Azure).

## Consequences

- Tokens without the `birrapoint-api` audience are rejected.
- Any future client whose tokens must reach the API needs the same mapper.
- The audience value in the realm and in the API configuration must stay in sync per environment.
