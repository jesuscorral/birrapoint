# 0009 - JWT audience validation for the API

**Status:** Accepted
**Date:** 2026-07-17

## Context

T011 wired JWT bearer auth against Keycloak but left `TokenValidationParameters.ValidateAudience =
false`, with a comment noting no audience mapper existed yet on the `birrapoint-spa` client — issuer
and signature validation (via `Authority`) were the only trust boundary. `Program.cs` carried a
startup warning flagging this as a tracked gap that had to close before the first protected
endpoint went live, since disabling audience validation means any token issued by the realm for
*any* client (including ones added later, e.g. a future admin tool) would be accepted by this API.

T017 is that first endpoint (`GET /api/v1/styles`), so the gap had to close now, not be carried
forward again.

## Decision

Add an `oidc-audience-mapper` protocol mapper directly on the `birrapoint-spa` client in
`infra/keycloak/birrapoint-realm.json`, stamping a custom audience value (`birrapoint-api`) onto
every access token. The mapper is attached at the client level, not via a new default client scope
— a client-scope-based approach would require re-declaring `defaultClientScopes` on the client,
risking silently dropping the implicit `roles` scope that `KeycloakRolesClaimsTransformation`
depends on for the `realm_access.roles` claim.

`AuthenticationExtensions.cs` now sets `ValidateAudience = true` and `ValidAudience` from a new
`Keycloak:ApiAudience` configuration key, injected locally by `AppHost.cs`
(`Keycloak__ApiAudience=birrapoint-api`).

## Consequences

- Tokens without the `birrapoint-api` audience are rejected outright, closing the gap the T011
  comment and the `Program.cs` startup warning both flagged.
- Any new OIDC client added to the realm in the future (service tooling, a second frontend, etc.)
  must also carry this mapper — or a token from it will fail JWT validation the moment audience
  checking sees it, by design.
- The startup warning and its `IOptionsMonitor<JwtBearerOptions>` check in `Program.cs` are removed
  now that the condition it guarded against no longer holds.
- Production/`azd` deployment must set `Keycloak__ApiAudience` consistently with whatever audience
  value the production realm's mapper stamps (tracked with the rest of T096's env var wiring).
