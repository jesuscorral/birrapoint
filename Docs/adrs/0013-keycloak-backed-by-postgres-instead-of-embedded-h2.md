# 0013 - Keycloak backed by Postgres instead of dev-mode embedded H2

**Status:** Accepted
**Date:** 2026-08-05

## Context

Keycloak's `start-dev` mode defaults to an embedded, single-file H2 database (its own
`keycloakdb.mv.db` MVStore file — unrelated to the `keycloakdb` Aspire *resource* name this ADR
introduces below, a naming coincidence worth not confusing). ADR 0001 set up Keycloak via a
generic `AddContainer`; a later change (commit `47a77f4`) added a host bind mount of
`infra/keycloak/.data/h2` onto that H2 file specifically so runtime state — self-registered
organizer accounts, judges provisioned via the Admin API — survives container recreation across
`dotnet run` restarts, instead of resetting to just the imported realm seed every time.

That bind mount kept the *file* around, but H2's MVStore is not crash-safe: it has no WAL/fsync
discipline comparable to a real RDBMS. In practice, any ungraceful stop of the Keycloak
container — a Docker Desktop restart, the host going to sleep, a force-killed AppHost process —
risks corrupting the store. This was found 2026-08-05 while debugging exactly that symptom: an
organizer who had self-registered and imported 45 beer entries + a judge could no longer log in
after restarting the dev environment. Keycloak's logs showed `org.h2.mvstore.MVStoreException:
Reading from file ... failed` on that container's last cold start; the user record was still
resolvable by username, but its credential no longer validated. The competition data itself
(Postgres, a separate resource) was completely intact — only the Keycloak-side credential was
lost, silently, with no error surfaced to the organizer beyond "wrong password."

The AppHost already runs a real PostgreSQL 16 instance (`postgres`, `.WithDataVolume()`,
`ContainerLifetime.Persistent`) for the app's own data, per the constitution's pinned stack.
Keycloak has supported Postgres as a `KC_DB` backend since well before the version in use here
(26.2), with the JDBC driver bundled in the standard image — no extra provider install needed.

## Decision

Add a second logical database on the same Postgres server (`postgres.AddDatabase("keycloakdb",
"keycloak")`) and point the Keycloak container at it via `KC_DB=postgres` plus three values read
directly off the Postgres resource rather than hand-built — `KC_DB_URL` from
`keycloakDb.Resource.JdbcConnectionString`, `KC_DB_USERNAME` from
`postgres.Resource.UserNameReference`, `KC_DB_PASSWORD` from `postgres.Resource.PasswordParameter`
(the same generated password the `db` database already uses) — so a future rename of the database
or an explicit username on the `postgres` resource can't silently drift out of sync with what
Keycloak is told to connect to. Drop the `infra/keycloak/.data/h2` bind mount entirely — H2 is no
longer in the picture. Keep the two remaining bind mounts (`infra/keycloak` for realm import,
`infra/keycloak/themes/birrapoint` for the custom login theme), which are read-only config, not
runtime state, and were never the problem. To force a realm re-import (e.g. after editing
`birrapoint-realm.json`) or to recover from a corruption incident like this one, drop the
`keycloak` database on the Postgres container before the next `dotnet run` — there is no longer a
folder to clear.

## Consequences

- **Positive**: Keycloak's runtime state now has the same WAL/fsync durability guarantee as the
  app's own data, on infrastructure we already run and already trust — no new dependency, no new
  container, one more logical database on an existing server. The awkward host-bind-mount
  permission workaround from ADR 0001's era (a fresh named volume being created root-owned,
  clashing with H2's non-root `keycloak` user) is moot once H2 is gone.
- **Negative**: switching backend means the *existing* H2-stored state (whatever the corrupted
  file still held) does not migrate forward — Keycloak reinitializes a fresh schema on `keycloakdb`
  via its own Liquibase changelogs on first boot after this change, and the realm re-imports from
  `birrapoint-realm.json` (`IGNORE_EXISTING`, so this is a one-time reset, not a recurring cost).
  Any self-registered users existing only in the old H2 file need to be recreated (same email,
  new self-registration) — and since Keycloak's Admin API does not honor a caller-supplied user
  `id` on creation, any Postgres-side data keyed by the *old* Keycloak `sub` (`Organizers
  .KeycloakUserId`, `Competitions.CreatedByUserId`, and similarly `Judges.KeycloakUserId` for any
  already-provisioned judge) has to be re-pointed at the new `sub` by hand, once, as part of this
  migration — done directly against Postgres for the one affected organizer account as part of
  landing this change.
- **Negative**: Keycloak's `KC_DB=postgres` switch triggers a Quarkus re-augmentation plus a full
  Liquibase schema build on its first boot against the new database — noticeably slower than H2's
  cold start. Nothing in the AppHost `WaitFor`s Keycloak specifically being *ready* (ADR-0001's
  own noted gap, "no built-in health check"), so this widens an already-latent cold-start race
  rather than introducing a new one; a manual `WithHttpHealthCheck` against `/realms/birrapoint`
  (ADR-0001's suggested mitigation) is now more worth doing.
- **Negative**: wiping the Postgres data volume now resets Keycloak (realm, users, client
  secrets) and the app's own data together, as one unit, instead of independently — there was no
  such coupling when Keycloak's state lived in a separate bind-mounted file.
- **Negative**: Keycloak connects as the Postgres superuser (`postgres`), so it can read/write the
  `birrapoint` database too — no isolation between the identity store and application data. Fine
  for local dev; Phase 16's real deployment should give Keycloak its own least-privilege role
  scoped to just the `keycloak` database.
- **Negative**: Phase 16's not-yet-built backup job (`infra/backup/`, a `pg_dump`) will need to
  back up and restore `keycloak` in lockstep with `birrapoint` — restoring one without the other
  reproduces the exact `sub`-mismatch this ADR's Context section describes needing to fix by hand.
- **Review trigger**: revisit for Phase 16 — dedicated least-privilege DB role for Keycloak
  instead of the shared superuser, and re-evaluate the health-check gap above. Otherwise none
  expected day to day: Postgres-backed Keycloak is the standard production-grade configuration
  Keycloak itself recommends over dev-mode H2.
