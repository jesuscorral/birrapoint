---
description: Check whether the local Aspire stack (Postgres, Keycloak, Mailpit, API, PWA) is up
---

Report the local stack status without starting or stopping anything:

1. `docker info --format '{{.ServerVersion}}'` — if it fails, Docker Desktop is down; stop here.
2. `docker ps --format '{{.Names}}\t{{.Status}}\t{{.Ports}}'` — look for `postgres-*`,
   `keycloak-*`, `mailpit-*`.
3. Probe each endpoint with `curl -sk -o /dev/null -w '%{http_code}' --max-time 5 <url>`:
   API `http://localhost:5121/alive`, PWA `http://localhost:4200`, Keycloak
   `http://localhost:8081/realms/birrapoint`, Mailpit `http://localhost:8025`, Aspire dashboard
   `https://localhost:17202`.

Reply with one line per service (up / down + code). If something is down, suggest the fix:
`dotnet run --project backend/src/BirraPoint.AppHost` for the stack; Keycloak missing while
Postgres runs usually means Docker Desktop's C: sharing broke (restart Docker Desktop — ask the
user first). Never drop databases or volumes.
