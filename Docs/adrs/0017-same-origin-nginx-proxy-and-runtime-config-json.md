# 0017 - Same-origin nginx reverse proxy and runtime `/config.json` for the PWA

**Status:** Accepted
**Date:** 2026-09-24

## Context

The PWA compiled its Keycloak and API URLs into the bundle (`environment.ts`), which breaks FR-043
(no environment-specific configuration in images) — the API URL is only known after
`terraform apply`. CORS existed only in Development, and the production topology was undecided.

## Decision

1. **Runtime configuration**: the PWA fetches `/config.json` (`{ keycloak: { url, realm,
   clientId }, apiBaseUrl }`) before bootstrap and provides it as `APP_CONFIG`. Locally `ng serve`
   serves `public/config.json`; the container writes it from `KEYCLOAK_*`/`API_BASE_URL` at start
   and refuses to start without the required values.
2. **Offline boot**: `/config.json` is an ngsw *data* group with the `freshness` strategy (an asset
   group would fail its hash check).
3. **Same origin**: the web container's nginx serves the PWA and proxies `/api/` and `/hubs/`
   (WebSocket upgrade) to the API; `apiBaseUrl` is `""`. The API has **internal ingress only**;
   nginx resolves it per request so the web app starts before the API exists.
4. **API behind a proxy**: outside Development, `UseForwardedHeaders` (known networks cleared) and
   no HTTPS redirection (TLS ends at ACA ingress); HSTS stays.

## Consequences

- One web image for every environment; no production CORS; the API is not publicly reachable.
- One extra in-environment hop; upload size and hub timeouts are nginx settings
  (`client_max_body_size 20m`, 1 h hub timeouts).
- Bootstrap waits for one small request, served from the service-worker cache offline.
- Forwarded headers are trusted from any source: narrow this before ever giving the API external
  ingress.
