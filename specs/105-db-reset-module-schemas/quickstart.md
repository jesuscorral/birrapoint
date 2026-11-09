# Quickstart: validate database reset and module schemas

Prerequisites: Docker Desktop, .NET 10 SDK, Node 24+ (as in `CLAUDE.md`).

## 1. Old database is refused (US17, FR-070)

On a workstation whose local volume was built with the old migrations:

```bash
dotnet run --project src/Aspire/BirraPoint.AppHost
```

Expected: the `api` resource fails at startup with a log line saying the database was created by the
previous migrations and naming the **Reset database** command. Postgres, Keycloak and Mailpit stay up.

## 2. Reset keeps identity accounts (US17, FR-071, SC-017)

In the Aspire dashboard (`https://localhost:17202`), open the `db` resource → **Reset database**
(or through the Aspire MCP: `execute_resource_command` on `db`, from the main session).

Expected: `api` restarts and becomes healthy. Sign in at `http://localhost:4200` with an account that
existed before (e.g. the seeded `organizer` / `organizer`, or a self-registered one); the sign-in
works and the competition list is empty.

## 3. Fresh volume builds from one baseline (US15, SC-014, SC-018)

With an empty volume (or after step 2): sign in as `organizer`, create a draft competition and pick
a BJCP style in the categories step. Expected: styles are listed and the draft saves.

Check the migration history:

```sql
SELECT "MigrationId" FROM migrations."__EFMigrationsHistory";   -- exactly one row: *_InitialCreate
```

## 4. Every table is in its owner's schema (US16, SC-015)

```sql
SELECT table_schema, table_name FROM information_schema.tables
WHERE table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY 1, 2;
```

Expected: exactly the mapping in [data-model.md](./data-model.md); no row with `table_schema = 'public'`.
The `SchemaTests` integration suite asserts the same automatically.

## 5. Nothing else changed (FR-074, SC-016)

```bash
dotnet test tests/BirraPoint.Api.UnitTests
dotnet test tests/BirraPoint.Api.IntegrationTests
dotnet test tests/BuildingBlocks/BuildingBlocks.Jobs.Tests
dotnet test tests/BuildingBlocks/BuildingBlocks.Audit.Tests
cd src/Birrapoint.Web && npm run e2e      # needs the running stack
```

Expected: all green with no assertion changed (only new tests added).
