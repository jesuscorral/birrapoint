# Feature Specification: Database reset, single baseline migration, one schema per module

**Feature Branch**: `feature/105-db-reset-module-schemas`

**Created**: 2026-11-08

**Status**: Draft

**Input**: GitHub issue #105 — "Reset database: single baseline migration, one schema per module". The app is not in production, so the current database and its 14 incremental migrations can go. Each module's tables move into their own schema to make module ownership visible in the database, as a first step towards modules that can be extracted as independent services (practice project: extra design accepted for learning).

**Baseline**: `specs/001-birrapoint-mvp/` (US1–US14, FR-001–FR-065, SC-001–SC-013). Ids in this spec continue that sequence.

## User Scenarios & Testing *(mandatory)*

The actors are the people who build and run BirraPoint: developers on their workstation and the
operator who deploys it. Organizers and judges see no change.

### User Story 15 - Fresh database from a single baseline (Priority: P1)

A developer starts the local stack on an empty database volume and gets a complete, working
database — every table, constraint and the BJCP style catalog — built from one baseline instead of
replaying 14 historical steps.

**Why this priority**: It is the core of the change and the precondition for the others: every later
module split (#106, #107) starts from this baseline.

**Independent Test**: Delete the local database volume, start the stack with its single start
command, sign in as the seeded organizer and create a competition that uses a BJCP style.

**Acceptance Scenarios**:

1. **Given** an empty database volume, **When** the developer starts the local stack, **Then** the application database is created from exactly one baseline migration with no manual step, and the BJCP catalog is available.
2. **Given** the database built from the baseline, **When** every existing automated suite (unit, integration, end-to-end) runs, **Then** all pass without changes to their expectations.
3. **Given** the database built from the baseline, **When** it is compared with one built from the previous 14 migrations, **Then** both have the same tables, columns, types, keys, indexes, constraints and seed data (only the schema each table lives in differs).

---

### User Story 16 - Data ownership visible per module (Priority: P2)

A developer inspecting the database sees each module's data grouped under that module's name
(Catalog, Organization, Judging, Publication) and the shared building blocks' data under their own
names (Jobs, Audit), with nothing left in the default area.

**Why this priority**: It makes module boundaries visible where they are hardest to keep — the data —
and prepares each module to own its storage later.

**Independent Test**: List the database's schemas and the tables in each one, and check every table
against the module that owns it.

**Acceptance Scenarios**:

1. **Given** the baseline database, **When** the developer lists the tables per schema, **Then** each table appears only in the schema of the module (or building block) that owns it, and the default schema holds no application table.
2. **Given** the baseline database, **When** a table of one module references a table of another module, **Then** the reference still works exactly as before (this feature keeps cross-module references; removing them is #107).

---

### User Story 17 - Reset only the application data (Priority: P3)

A developer resets the local application database to the baseline with one documented command,
without losing the identity provider's accounts that live on the same database server.

**Why this priority**: Developers whose database was built with the old migrations need a safe way to
move to the baseline; losing the seeded or self-registered identity accounts would cost them their
logins.

**Independent Test**: With an existing local database built from the old migrations, run the
documented reset, restart the stack, and sign in with an account that existed before the reset.

**Acceptance Scenarios**:

1. **Given** a local database built from the old migrations, **When** the developer runs the documented reset and restarts the stack, **Then** the application database is rebuilt from the baseline and competition data is gone.
2. **Given** the same reset, **When** the developer signs in with an identity account created before the reset, **Then** the sign-in succeeds (the identity store was not touched).
3. **Given** a local database built from the old migrations and no reset, **When** the stack starts, **Then** startup stops with a clear message that names the reset command, instead of failing midway or silently mixing old and new structures.

### Edge Cases

- The database volume holds the old migration history: startup refuses to proceed and points to the reset (FR-070), it never tries to apply the baseline over existing tables.
- The reset command runs while the stack is up: it must either stop the affected services first or fail with a message saying to stop them; it never leaves a half-dropped database.
- The deployed (cloud) database: none has ever been created, so no cloud data migration exists; a deployment against an empty database behaves as US15.
- Integration tests create throwaway databases: they must build from the same baseline and see the same schemas.
- A building block (Jobs, Audit) is reused by another host: its schema name must be set by the host, not hard-coded to BirraPoint.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-066**: The application database MUST be buildable from a single baseline migration that produces the complete current data model, including the BJCP catalog seed (FR-049), replacing all previous migrations.
- **FR-067**: The baseline MUST be equivalent to the current data model: same tables, columns, types, nullability, keys, indexes, unique constraints, check constraints and seed data; only the schema of each table changes.
- **FR-068**: Every application table MUST live in the schema of its owner: `catalog`, `organization`, `judging`, `publication` for the modules, `jobs` for the job queue and `audit` for the audit log. The default schema MUST hold no application table.
- **FR-069**: The schema used by a reusable building block (Jobs, Audit) MUST be configurable by the host application; BirraPoint configures `jobs` and `audit`.
- **FR-070**: At startup, if the database contains a migration history that does not belong to the new baseline, the application MUST stop with a message that identifies the problem and names the documented reset command.
- **FR-071**: A documented reset command MUST drop and recreate only the application database, leaving the identity provider's database on the same server untouched.
- **FR-072**: Starting the local stack on an empty volume MUST create the application database with no manual step (unchanged behaviour of the single start command).
- **FR-073**: The migration history record MUST live outside the default schema, in a location documented alongside the schemas.
- **FR-074**: Existing behaviour MUST not change: no API contract, realtime event, business rule or user-visible behaviour is altered, and every existing automated suite passes unchanged.
- **FR-075**: The data model documentation MUST show the schema of every table, and the decision (one schema per module, building-block schemas configurable) MUST be recorded as an architecture decision.

### Key Entities

- **Module schema**: a named area of the application database owned by exactly one module (Catalog, Organization, Judging, Publication) or building block (Jobs, Audit); holds only that owner's tables.
- **Baseline migration**: the single starting point that builds the full data model and seed; replaces the 14 historical migrations.
- **Migration history**: the record of which migrations a database has applied; used at startup to detect a database built from the old migrations.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-014**: From an empty volume, a developer has a working local stack (organizer can sign in and create a competition with a BJCP style) with one start command and zero manual database steps.
- **SC-015**: 100% of application tables are in their owner's schema; 0 application tables remain in the default schema.
- **SC-016**: 100% of existing automated tests pass with no change to their assertions.
- **SC-017**: After the documented reset, 100% of identity accounts that existed before it can still sign in.
- **SC-018**: The number of migrations to review drops from 14 to 1.

## Assumptions

- No deployed (cloud) database has ever been created, so no production data needs migrating and the cloud first deploy simply builds from the baseline.
- Losing all local application data (competitions, entries, evaluations) is accepted; identity accounts must survive.
- Detecting an old database and stopping with a clear message (FR-070) is preferred over resetting it automatically, so no developer loses data without asking for it.
- Cross-module references (foreign keys between schemas) are kept in this feature; removing them belongs to #107, and integration events to #106.
- The current single application data context stays; one context per module is #107.
- Schema names are lower-case module names; the migration history goes to a dedicated location (decided in the plan).
