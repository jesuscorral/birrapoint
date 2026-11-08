# 0041 - One PostgreSQL schema per module and a single baseline migration

**Status:** Accepted
**Date:** 2026-11-08

## Context
All tables lived in `public`, so module ownership (ADR-0033/0034) was invisible in the database. The
app is not in production, and 14 incremental migrations had accumulated, several undoing earlier
ones. The long-term goal is modules that can be extracted as independent services (practice project).
Issue #105.

## Decision
- **Schema per module**, derived from the entity's module namespace: `catalog`, `organization`,
  `judging`, `publication`. Building blocks keep no schema of their own; the host sets it when it
  maps the block: `jobs` (`DispatchJobs`) and `audit` (`AuditLogs`). EF history lives in `migrations`
  (`__EFMigrationsHistory`, set in `AppDbContext.OnConfiguring`). `public` holds no table. Table
  names do not change. Table map: `specs/105-db-reset-module-schemas/data-model.md`.
- **Single baseline**: the 14 migrations are deleted and replaced by one `InitialCreate` that
  creates the schemas, tables and the BJCP seed (ADR-0005). It was checked against the SQL of the old
  chain; only schema qualifiers differ, plus three column defaults the old chain had left and the
  model never declared: `AbvPercent DEFAULT 0.0`, `BjcpStyles.DescriptionJson DEFAULT '{}'`,
  `SubmittedAt DEFAULT '-infinity'`. EF always supplies those values, so behaviour is unchanged.
- **Legacy-database guard**: `LegacyDatabaseGuard` runs before `MigrateAsync`. It stops startup,
  naming the reset command, if `public."__EFMigrationsHistory"` exists or the new history holds an
  id the assembly does not know. Nothing is dropped automatically.
- **Reset command**: Aspire resource command "Reset database" on `db`. It runs
  `DROP DATABASE "birrapoint" WITH (FORCE)` + `CREATE DATABASE` on the server's `postgres`
  database, then restarts `api`. The `keycloak` database is never touched, so identity accounts
  survive.
- **Transitional convention**: one `AppDbContext` still maps every module, so the schema is chosen by
  entity namespace. Issue #107 replaces this with one context per module, each with its own default
  schema.

## Consequences
- Module ownership is visible in SQL and in the database tooling; extraction later starts from a
  clean boundary.
- One migration to review instead of 14. Developers with an old local volume must run the reset
  once; competition data is lost (acceptable, no production).
- Cross-schema foreign keys remain (for example `judging.TableSamples -> organization.BeerEntries`,
  `organization.CompetitionCategoryStyles -> catalog.BjcpStyles`, `jobs.DispatchJobs ->
  organization.Competitions`). They still couple modules at the database level; removing them is #107.
- Raw SQL must schema-qualify table names (the job worker already does).
- The namespace rule is implicit and temporary; a new entity outside the module namespaces would
  land in the wrong schema until #107, so a test asserts the full table map.
- Future migrations are incremental again on top of the baseline; the baseline must not be edited
  after it ships to a shared database.
