# 0006 - SignalR hub identity via `HubCallerContext.User`, not `ICurrentUser`

**Status:** Accepted
**Date:** 2026-07-13

## Context

REST slices resolve the caller through `ICurrentUser`, which reads `IHttpContextAccessor`. Hub
methods run over a persistent connection outside the request pipeline, so that accessor is not
reliable there. Browser WebSocket handshakes also cannot send an `Authorization` header.

## Decision

- `CompetitionHub` reads identity only from `Context.User`; reviewers flag hub code that injects
  `ICurrentUser`.
- `JwtBearerEvents.OnMessageReceived` accepts the token from `?access_token=` **only** on
  `/hubs/competition`; every other endpoint still requires the header.

## Consequences

- Helpers shared by REST and hub authorization must take a `ClaimsPrincipal`, not
  `ICurrentUser`. Role mapping (`KeycloakRolesClaimsTransformation`) works the same for the hub.
- Query-string tokens can reach access logs; exposure is limited to the hub path, and the web
  nginx access log omits query strings.
