# Feature Specification: One data context per module, no cross-module foreign keys

**Feature Branch**: `feature/107-module-dbcontexts`

**Created**: 2026-11-27

**Status**: Approved

**Input**: GitHub issue #107 — "One DbContext per module, no cross-module foreign keys". With schemas (#105) and integration events (#106) in place, each module (Catalog, Organization, Judging, Publication) gets its own data context mapped only to its schema, with its own migrations and migration history. A module can then no longer read or write another module's tables, so it can later be split into its own service with its own database. Still one deployable API and one physical database. Practice project: extra design accepted for learning.

**Baseline**: `specs/001-birrapoint-mvp/` (US1–US14, FR-001–FR-065, SC-001–SC-013), `specs/105-db-reset-module-schemas/` (US15–US17, FR-066–FR-075, SC-014–SC-018) and `specs/106-integration-events-outbox/` (US18–US21, FR-076–FR-091, SC-019–SC-024). Ids in this spec continue that sequence.

## Clarifications

### Session 2026-11-27

- Q: May Judging keep a local copy of the entry and judge data it needs, or does it ask Organization through its contracts on every read? → A: Contracts on every read, as today; no local copy in this feature (FR-094).
- Q: How do databases built with the single shared migration history move to per-context histories? → A: Reset with the documented command, as in #105; each context starts from its own baseline (FR-098).
- Q: Do the module's changes, audit entries, staged jobs and outbox events still commit together? → A: Yes; all contexts in a request share one connection and transaction (FR-096).

## User Scenarios & Testing *(mandatory)*

The actors are the developers and the operator who build and run BirraPoint. Organizers and judges
see no change: every existing flow behaves as before.

### User Story 22 - Each module owns its data alone (Priority: P1)

A developer working in one module can only reach that module's own tables. Another module's data is
reachable only through that module's public contracts (reads) or its integration events (writes).
The database holds no reference from one module's tables to another module's tables; references
between modules are plain identifiers.

**Why this priority**: It is the goal of the feature: without it no module can be split into its own
service with its own database.

**Independent Test**: Inspect the database and check there is no foreign key between schemas; run the
architecture tests and check they fail when a module is made to reach another module's data access or
entities; run every existing suite and check it passes unchanged in behaviour.

**Acceptance Scenarios**:

1. **Given** the migrated database, **When** the foreign keys are listed, **Then** none goes from one schema to another.
2. **Given** a module's data access, **When** its model is inspected, **Then** it maps only tables in that module's own schema (its outbox and inbox included).
3. **Given** a module's code, **When** it references another module's data access or entities, **Then** the architecture tests fail and name the offending type.
4. **Given** any existing organizer or judge flow, **When** it runs after the change, **Then** its result is the same as before (same responses, same realtime notifications, same audit entries).

---

### User Story 23 - Each data store migrates on its own (Priority: P2)

Each module, and each of the two cross-cutting building blocks (job queue, audit trail), has its own
set of migrations and its own migration history in its own schema. Starting the application brings
every store up to date in a fixed order; a developer adds a migration to one module without touching
the others.

**Why this priority**: Independent migrations are what a later split needs, but the system can run
with US22 before this is polished.

**Independent Test**: Start the application on an empty database and check every schema has its
tables and its own migration history; add a migration to one module only and check only that
module's history changes.

**Acceptance Scenarios**:

1. **Given** an empty application database, **When** the application starts, **Then** every module and building block store is created, each with its migration history in its own schema, in the documented order.
2. **Given** an up-to-date database, **When** the application starts again, **Then** no migration runs and startup is not slower than before beyond the target in SC-028.
3. **Given** a database built before this feature (one shared migration history), **When** the application starts, **Then** it stops with a clear message that names the documented reset command, instead of creating tables a second time or mixing histories.
4. **Given** a developer adds a migration to one module, **When** it is generated, **Then** it contains changes for that module's schema only and no other module's migrations change.

### Edge Cases

- A table in one module refers to a row of another module that no longer exists: today no flow deletes a competition, an entry, a judge or a style that another module refers to, so no orphan can appear; any future deletion across modules must go through an integration event (FR-095).
- A request writes its own module's data plus an audit entry, a background job and an outbox event: all of them still commit or roll back together (FR-096).
- A request reads another module's state through its contracts while it holds a lock or a transaction on its own data (e.g. closing a table checks the competition state): the read sees the same committed state it sees today, and no deadlock or second connection appears per request (FR-096).
- Two modules run migrations in parallel (several replicas start together): each store's migration is protected as today; the documented order is kept (FR-097).
- A migration in one module fails at startup: startup stops with an error naming the store; stores migrated earlier stay migrated.
- The style catalogue (Catalog) is referenced by style code from Organization: the code is still validated against the catalogue when entries and categories are written, without a database reference (FR-093).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-092**: Each module (Catalog, Organization, Judging, Publication) MUST have its own data context that maps only the tables in its own schema, including its own outbox and inbox (FR-077/FR-079). The job queue and the audit trail building blocks MUST each have their own data context in their own schema. The shared application data context MUST be removed.
- **FR-093**: References between modules MUST be identifiers only: no database foreign key between two schemas and no navigation from one module's entities to another module's entities. Values that used to be guaranteed by such a key (entry and category style codes, competition, entry, judge and table ids) MUST be validated when written, through the owning module's public contracts, with the same error responses as today.
- **FR-094**: Cross-module reads MUST go only through the owning module's public contracts; no module may query another module's tables, directly or through raw SQL.
- **FR-095**: Every operation that changes data in more than one module MUST keep its consistency through the integration events of FR-076–FR-081. Any future deletion of a row another module refers to MUST publish an integration event so the referring modules clean up their own data.
- **FR-096**: Within one request, a module's own changes, its audit entries, the background jobs it stages and the integration events it publishes MUST still commit or roll back together, and reads through other modules' contracts during that request MUST see the same state as today. Splitting the data contexts MUST NOT add a database connection per request per module.
- **FR-097**: Each data context MUST have its own migrations and its own migration history table in its own schema. At startup every context MUST be migrated in one documented, fixed order (building blocks first, then Catalog, Organization, Judging, Publication), with the same conditions as today (always in Development, opt-in elsewhere) and the same protection against concurrent runs.
- **FR-098**: The startup check for incompatible databases (FR-070, FR-091) MUST also stop startup, naming the documented reset command, when the database was built with the single shared migration history this feature replaces.
- **FR-099**: Automated architecture tests MUST fail when a module references another module's data context, entities, persistence configuration or schema, or when a context maps a table outside its own schema. A database-level test MUST fail if any foreign key crosses schemas.
- **FR-100**: Timestamps (`CreatedAt`/`UpdatedAt`) MUST keep being stamped centrally for every context, as today.
- **FR-101**: The decision (context per module, shared connection and transaction within a request, id-only references, migration order) MUST be recorded as an architecture decision, and the living data model, the living architecture document and the developer commands (adding and applying migrations per context) MUST be updated.

### Key Entities

- **Module data context**: the only data access of one module; maps the module's tables, its outbox and its inbox, all in the module's schema, plus its own migration history.
- **Building-block data context**: the data access of the job queue or the audit trail, in its own schema with its own migration history.
- **Cross-module reference**: an identifier (competition, entry, judge, table, style code) stored by one module about another module's row, without a database constraint; validated on write through the owner's contracts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-025**: 0 foreign keys between schemas in the migrated database, and 0 module types that reference another module's data access, entities or persistence configuration (architecture tests).
- **SC-026**: 100% of existing automated suites (unit, integration, end-to-end) pass; assertions change only where a test reached data access directly and now uses the owning module's context.
- **SC-027**: Each of the 6 data stores (4 modules, 2 building blocks) has its own migration history in its own schema after a start on an empty database.
- **SC-028**: Startup on an up-to-date database takes at most 2 s longer than before, and the existing read and write latency budgets (p95 reads < 200 ms, writes < 500 ms) still hold.

## Assumptions

- The application is not in production: databases built with the previous single migration history are reset with the documented command (ADR-0041), not migrated in place, as in #105. Each context starts from its own baseline migration.
- Judging keeps asking Organization through its public contracts on every read it needs (entries, judges, competition state); it does not keep a local copy. A local anonymous copy fed by events is left for a later issue if a split needs it.
- One physical database and one deployable API remain; the contexts share the request's database connection and transaction, so FR-096 needs no distributed transaction.
- Today no flow deletes a row that another module refers to (no deletion of competitions, entries, judges, tables or styles), so removing the cascading foreign keys changes no behaviour.
- The job queue's link to its competition (`CompetitionId`) becomes a plain identifier like every other cross-module reference.
- The old shared migration history schema is no longer used and is not created on a fresh database.
