# Research: Database reset, single baseline migration, one schema per module

All unknowns from the plan's Technical Context, resolved. Format: Decision / Rationale / Alternatives.

## R-105-1 How each table gets its schema

- **Decision**: One convention in `AppDbContext.OnModelCreating`, after `ApplyConfigurationsFromAssembly`: for every entity type whose CLR namespace starts with `BirraPoint.Api.Modules.<Module>.`, call `SetSchema("<module lower-case>")`. The two building-block entities get their schema explicitly in the existing `configure` lambdas: `ApplyJobs(job => job.ToTable("DispatchJobs", "jobs"))` and `ApplyAuditLog(configure: a => a.ToTable("AuditLogs", "audit"))`. Schema names live as constants in `Common/Persistence/DbSchemas.cs`.
- **Rationale**: Every entity already sits under its module's namespace (verified for all 21 module entities, including the import batches under `Organization/Features/Import/`), so one rule covers them all and new entities can't be forgotten. A schema test (SC-015) fails if any table lands elsewhere. #107 replaces the convention with `HasDefaultSchema` per module context, so the convention is deliberately transitional and lives in one place.
- **Alternatives**: `ToTable(name, schema)` in each of the 21 configuration classes, which is noisy (most configurations don't call `ToTable` today) and easy to miss for a new entity. `HasDefaultSchema`, which only supports one schema per context.

## R-105-2 Building-block schemas configurable by the host (FR-069)

- **Decision**: No change to `BuildingBlocks.Jobs` or `BuildingBlocks.Audit`. Their `Apply*` extensions already take a host `configure` callback that runs last, so `ToTable(name, schema)` there overrides the default. `JobSql` already builds `"schema"."table"` from the model (`entity.GetSchema()`), so the worker's raw `FOR UPDATE SKIP LOCKED` SQL follows. Add one block test per block proving that a host-set schema is honoured (the Jobs test runs a claim against a non-default schema).
- **Rationale**: The blocks stay free of BirraPoint knowledge (ADR-0035/0037) and the host decides.
- **Alternatives**: A `schema` parameter on `ApplyJobs`/`ApplyAuditLog`. That is redundant with `configure`, and it's a public API change across two blocks.

## R-105-3 Raw SQL with unqualified table names

- **Decision**: The five row-lock queries (`SELECT * FROM "TastingTables" … FOR UPDATE` in `CloseTableCommand`, `FixOrderCommand`, `LiveJudgeRemoval`; `"Competitions"` in `ChangeCompetitionStateCommand`, `NotifyJudgesCommand`) become schema-qualified literals: `judging."TastingTables"` and `organization."Competitions"`. `FromSqlInterpolated` turns every hole into a parameter, so identifiers can't be interpolated. The literals are kept, and the existing integration tests of those slices (which take the locks) prove them.
- **Rationale**: These are the only raw SQL against application tables (checked by grep on `FromSql|ExecuteSql|SqlQuery`; the job worker's SQL is already model-driven). Keeping literals avoids string-built SQL.
- **Alternatives**: Set `search_path` on the connection to every schema, which hides ownership and breaks as soon as two schemas share a table name. `FromSqlRaw` with a metadata-derived name, which means string concatenation into SQL for no real gain.

## R-105-4 Migration history location (FR-073)

- **Decision**: `__EFMigrationsHistory` in schema `migrations`, set in `AppDbContext.OnConfiguring` through `optionsBuilder.UseNpgsql(o => o.MigrationsHistoryTable("__EFMigrationsHistory", DbSchemas.Migrations))`. That overload adds Npgsql options without a connection string and merges with the caller's `UseNpgsql(connectionString)`. All seven construction sites (Program.cs, design-time factory, five test fixtures) get it without edits.
- **Rationale**: One place; test fixtures can't drift. A neutral schema keeps the history out of `public` and out of any one module's schema while there is still one context. In #107 each module context gets its own history table in its own schema.
- **Alternatives**: Pass the option at each `UseNpgsql` call site (7 places, easy to miss). Keep it in `public`, which FR-073 rejects.
- **Verify in implementation**: if the merge does not apply when options were built externally, fall back to a shared `UseBirraPointNpgsql(connectionString)` extension used by all sites.

## R-105-5 Baseline migration with the BJCP seed (FR-066, FR-067)

- **Decision**:
  1. Before deleting anything, generate the full SQL of the current 14 migrations (`dotnet ef migrations script`) as the reference.
  2. Delete `Migrations/*` and run `dotnet ef migrations add InitialCreate` on the schema-mapped model.
  3. Port the hand-written seed from `20260709090754_AddBjcpStyleCatalogDetails` into the new `InitialCreate.Up`: the code that reads the embedded `bjcp-2021.json` (ADR-0005, FR-049) and calls `InsertData`, now with `schema: "catalog"`.
  4. Generate the new script and diff it against the reference with schema prefixes stripped. The only accepted differences are the schema qualifiers, the `CREATE SCHEMA` statements, the history table location, and the removal of steps that later migrations undid (for example the `DELETE FROM "BjcpStyles"` re-seed and the `AuditLogs` timestamp back-fill).
  5. Record the diff result in the PR.
- **Rationale**: Proves FR-067 equivalence once, mechanically, without keeping the old migrations around.
- **Alternatives**: A permanent test comparing two models. There is nothing left to compare after the old migrations are deleted.

## R-105-6 Detecting a database built from the old migrations (FR-070)

- **Decision**: New `LegacyDatabaseGuard` in `Common/Persistence`, run in `Program.cs` right before `MigrateAsync`. It fails startup with `InvalidOperationException` naming the reset command when either:
  - `to_regclass('public."__EFMigrationsHistory"')` is not null (the old history location), or
  - the new history contains a migration id the assembly doesn't know.

  Unit-test the decision logic and integration-test both cases with Testcontainers.
- **Rationale**: Without it, an old local database would still migrate. The new history in `migrations` is empty, so EF creates fresh tables in the new schemas and leaves the old ones orphaned in `public`. That is the silent mix the spec forbids.
- **Alternatives**: Auto-drop in Development. The spec chose to stop instead, so no data is lost unasked.

## R-105-7 Reset command that keeps Keycloak (FR-071)

- **Decision**: An Aspire resource command on the `db` database resource, `WithCommand("reset-database", "Reset database", …)`, available in the dashboard and through the Aspire MCP/CLI. It runs, against the server's `postgres` maintenance database:

  ```sql
  DROP DATABASE "birrapoint" WITH (FORCE);
  CREATE DATABASE "birrapoint";
  ```

  It then restarts the `api` resource so startup migrates the baseline. The `keycloak` database on the same server is never named.

  If the AppHost can't reach Npgsql or restart the resource cleanly, fall back to `scripts/reset-db.ps1`:
  1. Find the Postgres container with `docker ps --filter`.
  2. Run `docker exec … psql -c 'DROP DATABASE "birrapoint" WITH (FORCE)'`.
  3. Tell the developer to restart the API.
- **Rationale**: `WITH (FORCE)` (Postgres 13+) closes the API's open connections, so a reset never leaves a half-dropped database while the stack runs (edge case). The command lives where the stack is defined and needs no container-name lookup. `dotnet ef database drop` is denied by the repo's permissions and `guard.js`, and it would need the design-time connection anyway.
- **Alternatives**: Removing the Docker volume, which also wipes Keycloak (forbidden by the spec and by the memory note on Keycloak users). A manual `psql`, which is not a documented single command.

## R-105-8 Cloud impact

- **Decision**: None. No cloud database has ever been created (living doc: neither Azure nor AWS applied). A first deploy migrates from the baseline through the existing `Database:MigrateOnStartup` path, unchanged.
- **Rationale / Alternatives**: n/a.
