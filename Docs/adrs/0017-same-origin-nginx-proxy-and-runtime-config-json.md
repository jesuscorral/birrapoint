# 0017 - Same-origin nginx reverse proxy and runtime `/config.json` for the PWA

**Status:** Accepted
**Date:** 2026-09-24

## Context

Until Phase 16 the Angular app took its Keycloak and API URLs from
`src/environments/environment.ts`, which is compiled into the bundle. That breaks FR-043 (images
must not contain environment-specific configuration): the web image would have to be rebuilt per
environment, and the API URL is only known after `terraform apply` creates the Container Apps
environment.

The API also enabled CORS only in Development. Program.cs left the production topology,
"same-origin behind ACA ingress or otherwise", as a Phase 16 decision.

## Decision

1. **Runtime configuration.** The PWA fetches `/config.json` (`{ keycloak: { url, realm,
   clientId }, apiBaseUrl }`) before `bootstrapApplication` and provides it through an
   `APP_CONFIG` injection token. `environment.ts` is removed. Locally `ng serve` serves
   `frontend/public/config.json`. In the container, `nginx/40-runtime-config.sh` writes the file
   from `KEYCLOAK_URL`/`KEYCLOAK_REALM`/`KEYCLOAK_CLIENT_ID`/`API_BASE_URL` at start, and a
   missing required value aborts startup.
2. **Offline boot (R-08).** `/config.json` is rewritten at runtime, so it cannot be in an ngsw
   asset group (hash mismatch). It is an ngsw data group with the `freshness` strategy instead:
   from the network when online, from the service-worker cache when offline.
3. **Same origin.** The web container's nginx serves the PWA and reverse-proxies `/api/` and
   `/hubs/` (with WebSocket upgrade for SignalR) to the API. In production `apiBaseUrl` is `""`.
   The API Container App has **internal ingress only**, and nginx reaches it at
   `https://<api>.internal.<env-domain>` (SNI on, `Host` = upstream name, public host/scheme in
   `X-Forwarded-*`), resolving it per request so the web app starts even while the API is still
   being provisioned.
4. **API behind a proxy.** Outside Development the API runs `UseForwardedHeaders` (known
   networks/proxies cleared, acceptable only because its ingress is internal) and no longer
   calls `UseHttpsRedirection`. TLS terminates at the ACA ingress, and redirecting the proxy's
   internal hop would break it. HSTS stays.

## Consequences

- One web image runs in any environment. No CORS policy is needed in production, and the API
  attack surface is not publicly reachable.
- Every browser request to the API passes through nginx: one more hop (sub-millisecond inside
  the environment), and upload size/timeouts are now nginx settings (`client_max_body_size 20m`,
  1 h hub timeouts).
- Bootstrapping waits on one extra small request. Offline, the service worker answers it from
  cache, so a judge who loaded the app once can still boot it without connectivity.
- Forwarded headers are trusted from any source. If the API ever gets external ingress, this
  must be narrowed first.
