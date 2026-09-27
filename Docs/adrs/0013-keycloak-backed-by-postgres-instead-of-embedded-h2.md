# 0013 - Keycloak backed by Postgres instead of dev-mode embedded H2

**Status:** Accepted
**Date:** 2026-08-05

## Context

Keycloak's `start-dev` uses an embedded H2 file, which a bind mount kept across restarts. H2's
MVStore is not crash-safe: after an ungraceful stop (Docker restart, host sleep) an organizer could
no longer log in (`MVStoreException` on startup, credential no longer valid), while the app's own
Postgres data was intact. The AppHost already runs PostgreSQL 16, and Keycloak 26 supports it out of
the box.

## Decision

Add a second logical database on the same server (`postgres.AddDatabase("keycloakdb",
"keycloak")`) and point Keycloak at it with `KC_DB=postgres`, taking URL, user and password from
the Aspire resources rather than hand-built strings. Remove the H2 bind mount; keep the read-only
realm-import and theme mounts. To force a realm re-import, drop the `keycloak` database before the
next run.

## Consequences

- Keycloak state gets the same durability as the app data, with no new container.
- First boot on Postgres is slower (schema build), which widens the readiness gap noted in
  ADR-0001.
- Wiping the local Postgres volume resets Keycloak and app data together.
- A recreated user gets a new `sub`; rows keyed by the old one must be re-pointed by hand
  (`Organizers.KeycloakUserId`, `Competitions.CreatedByUserId`, `Judges.KeycloakUserId`).
- Locally Keycloak connects as the Postgres superuser. In production each database has its own
  Neon role, and both databases share one Neon branch so a point-in-time restore keeps them
  consistent (ADR-0016).
