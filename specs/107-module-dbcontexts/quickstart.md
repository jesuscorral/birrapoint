# Quickstart: One data context per module

Validation scenarios for [spec.md](./spec.md). Prerequisites: the CLAUDE.md §Commands toolchain
and Docker. This feature replaces the migration history, so a local database built before it is
refused at startup and must be reset (Aspire dashboard → `db` → **Reset database**, ADR-0041).

## Automated

```bash
dotnet test tests/BirraPoint.Api.UnitTests --filter "FullyQualifiedName~Architecture"         # boundaries + context models
dotnet test tests/BirraPoint.Api.UnitTests --filter "FullyQualifiedName~LegacyDatabaseGuard"
dotnet test tests/BuildingBlocks/BuildingBlocks.Messaging.Tests                               # two-context routing
dotnet test tests/BirraPoint.Api.IntegrationTests --filter "FullyQualifiedName~Persistence"   # schema isolation, shared tx, ids
dotnet test tests/BirraPoint.Api.UnitTests && dotnet test tests/BirraPoint.Api.IntegrationTests   # SC-026
cd src/Birrapoint.Web && npm run e2e                                                          # SC-026
```

## §1 No foreign key crosses schemas (US22 scenario 1, SC-025)

Integration test, also runnable by hand against the local stack:

```sql
SELECT tc.table_schema, tc.table_name, ccu.table_schema AS ref_schema, ccu.table_name AS ref_table
FROM information_schema.table_constraints tc
JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = tc.constraint_name
WHERE tc.constraint_type = 'FOREIGN KEY' AND ccu.table_schema <> tc.table_schema;
```

Expected: 0 rows.

## §2 One history per store (US23 scenario 1, SC-027)

Start on an empty database. Expected: `jobs`, `audit`, `catalog`, `organization`, `judging`,
`publication` each contain `__EFMigrationsHistory` with one `InitialCreate` row; schema `migrations`
does not exist; `catalog.BjcpStyles` is seeded.

## §3 Old database refused (US23 scenario 3, FR-098)

Start the stack on a database built by `main` before this feature. Expected: the API stops with the
message naming **Reset database**. Run it, restart: the API starts and a pre-existing Keycloak
account signs in.

## §4 Atomic writes across contexts (FR-096)

Integration tests: an organizer correction whose save fails after staging rolls back the module row,
the audit entry, the staged job and the outbox event together; a successful one commits all four;
same for the job worker and the inbox runner paths.

## §5 Boundary test catches a violation (US22 scenario 3, FR-099)

Temporarily inject `OrganizationDbContext` into a Judging handler and run the architecture tests.
Expected: a failure naming the handler type. Revert.

## §6 Behaviour unchanged (US22 scenario 4)

Run the US1–US14 quickstart flows on the local stack (create, import, tables, judge, close, finalize,
results). Expected: same screens, realtime updates and downloads as before.

## §7 Budgets (SC-028)

Compare API startup time before/after (Aspire console logs, "Application started"); run
`k6 run infra/perf/api-budgets.js`. Expected: ≤ 2 s slower start; p95 reads < 200 ms, writes < 500 ms.
