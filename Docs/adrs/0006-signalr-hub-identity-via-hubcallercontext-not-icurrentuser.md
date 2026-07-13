# 0006 - SignalR hub identity via `HubCallerContext.User`, not `ICurrentUser`

**Status:** Accepted
**Date:** 2026-07-13

## Context

T015 introduces `CompetitionHub`, the project's first SignalR endpoint. Every REST slice so far
resolves the caller through `ICurrentUser` (`Common/Auth/CurrentUser.cs`, T011), which reads
`IHttpContextAccessor.HttpContext?.User`. That accessor is populated for the lifetime of a single
HTTP request by ASP.NET Core's request pipeline. A SignalR hub method, however, runs over a
persistent WebSocket connection outside that per-request pipeline — `IHttpContextAccessor` is not
guaranteed to reflect the connection's authenticated principal during a hub method invocation, so
reusing `ICurrentUser` inside `CompetitionHub` would be unreliable in a way that is easy to miss in
local testing (single connection, low concurrency) and to hit in production.

Separately, browser WebSocket clients cannot attach a custom `Authorization` header to the
handshake request that `@microsoft/signalr` issues, so the standard JWT-bearer `Authorization:
Bearer <token>` flow used by every REST endpoint does not work for the hub connection.

## Decision

- `CompetitionHub` reads the caller's claims from `Context.User` (`HubCallerContext.User`,
  provided directly by SignalR for the connection), never from `ICurrentUser`. This applies to
  every future hub method, not just T015's — a reviewer should flag any hub code that injects
  `ICurrentUser` instead of using `Context.User`.
- `AuthenticationExtensions.AddKeycloakAuthentication` adds a `JwtBearerEvents.OnMessageReceived`
  handler that reads the token from the `access_token` query-string parameter, but only when the
  request path starts with `/hubs/competition`. Every other endpoint still requires the standard
  `Authorization` header — this is additive, not a relaxation of the existing REST auth flow.

## Consequences

- Hub authorization logic (role checks, DB-backed ownership/membership checks) reads
  `Context.User` directly; it cannot call the existing `ICurrentUser` service, so any shared
  helper extracted later for both REST and hub code must take a `ClaimsPrincipal` parameter rather
  than depending on `ICurrentUser`.
  `KeycloakRolesClaimsTransformation` (T011) still runs identically for the hub's handshake
  request, since it is a standard ASP.NET Core authentication pipeline component and does not
  depend on `IHttpContextAccessor` — `Context.User.IsInRole(...)` works exactly like it does on a
  REST request.
- The query-string token path is a well-known, narrower attack surface than the header (tokens can
  end up in server access logs); scoping `OnMessageReceived` to `/hubs/competition` only limits
  that exposure to the one endpoint that genuinely needs it, rather than accepting
  `?access_token=` globally.
- No new package or abstraction was introduced — this only changes which existing ASP.NET Core
  primitive (`Context.User` vs. `IHttpContextAccessor.HttpContext.User`) a hub reads from.
