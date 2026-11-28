# Feature Specification: One project per module with flat vertical slices

**Feature Branch**: `feature/141-module-projects`

**Created**: 2026-11-28

**Status**: Approved

**Input**: GitHub issue #141 — "Reorganize the backend into one project per module with flat vertical slices". Each business module (Catalog, Organization, Judging, Publication) becomes its own project with compiler-enforced boundaries; cross-module types move to one shared contracts project; slices become flat `<Area>/<Slice>/{Endpoint,Request,Handler,Response}` folders with one setup file per area; writes go through aggregate methods and reads are plain no-tracking projections; one module shape; a module reference document. Reference: baranacikgoz/modular-monolith-ddd-vsa-webapi, taking only the ideas that pay off without split deployments. Pure refactor: no behaviour, API, payload, error or schema change. MediatR 12.x, the validation pipeline, the pinned stack and the constitution stay as they are.

**Baseline**: `specs/001-birrapoint-mvp/` (US1–US14, FR-001–FR-065, SC-001–SC-013), `specs/105-db-reset-module-schemas/` (US15–US17, FR-066–FR-075, SC-014–SC-018), `specs/106-integration-events-outbox/` (US18–US21, FR-076–FR-091, SC-019–SC-024) and `specs/107-module-dbcontexts/` (US22–US23, FR-092–FR-101, SC-025–SC-028). Ids in this spec continue that sequence. They describe structural rules, not product behaviour: no product requirement is added or changed.

## Clarifications

### Session 2026-11-28

- Q: One PR or several? → A: Six sequential PRs, one per delivery step of FR-111. Spec, plan and tasks land with the first; each later PR branches from `main` after the previous one merges; all reference #141 and the last one closes it.
- Q: Rename the host project? → A: Yes, `BirraPoint.Api` becomes `BirraPoint.Host`; the Dockerfile path in the CI and release workflows, the PR labeler globs and the Aspire AppHost reference follow (FR-110). No Terraform change.
- Q: Split the unit test project per module? → A: No; the existing unit and integration test projects stay, with folders mirroring the modules and access to each module's internals granted explicitly (FR-110).

## User Scenarios & Testing *(mandatory)*

The actors are the developers who maintain BirraPoint and the reviewers who read their changes.
Organizers and judges see no change: every flow, response, realtime notification and email behaves
exactly as before.

### User Story 24 - A module cannot reach another module by accident (Priority: P1)

A developer working in one module cannot use another module's internals: the build fails as soon as
the code names a type that belongs to another module. Anything a module offers to the others
(contract interfaces, snapshots, integration events) lives in one shared contracts place, grouped by
the module that owns it.

**Why this priority**: It turns today's test-time boundary check into a build-time one and is the
backbone the rest of the reorganization hangs on.

**Independent Test**: Add a reference from one module to an internal type of another module and check
the build fails; remove it and check the build and every existing suite pass.

**Acceptance Scenarios**:

1. **Given** the reorganized backend, **When** a module's code names a type internal to another module, **Then** the build fails with an error naming that type.
2. **Given** the reorganized backend, **When** a module needs data or behaviour from another module, **Then** it uses only types from the shared contracts place, under the owning module's group.
3. **Given** the reorganized backend, **When** the dependency graph of the backend parts is listed, **Then** no module part depends on another module part, and the shared kernel and the shared contracts depend on no module.

---

### User Story 25 - A developer finds any slice by its name (Priority: P1)

A developer looking for "create table" or "submit evaluation" opens the module, the area and the
slice folder, and finds the same four predictable files in every slice: the route, the request (with
its validation), the handler and, only when there is a body, the response. Each area maps its routes
in one place. Helpers shared inside an area sit in one shared folder of that area.

**Why this priority**: Navigation and review cost is the main complaint of the issue; it delivers value
on its own, module by module.

**Independent Test**: Pick any endpoint from the REST contract and locate its slice from the module and
area names alone; run the structure check and see it pass, then add a file that breaks the naming
rule and see it fail.

**Acceptance Scenarios**:

1. **Given** any endpoint in the REST contract, **When** a developer looks for it, **Then** it lives in `<Module>/<Area>/<Slice>/` with files named `Endpoint`, `Request`, `Handler` and, when the slice returns a body, `Response`.
2. **Given** any area, **When** its routes are mapped, **Then** a single setup file of that area maps all of them.
3. **Given** the reorganized backend, **When** the source tree is searched, **Then** no nested `Features/*/Features/` folder, no `CommonComponents/` folder, no `Base<X>Endpoints` file and no `<M>ModuleEndpoints` file remains.

---

### User Story 26 - Business rules live on the thing they protect (Priority: P2)

A developer who wants to know when a competition may change state, how a score is capped, when a
sheet locks or reopens, or when a table may close, reads one method on the owning aggregate. Write
handlers load the aggregate, call its method and save; read handlers only project data and never load
an aggregate for change.

**Why this priority**: It makes the non-negotiable invariants (state machine, scoring, immutability)
easy to find and test, but the structure of US24/US25 is useful without it.

**Independent Test**: Run the architecture tests and check they pass; make a read handler load a
tracked aggregate, or make a write handler change an aggregate's state without its method, and check
they fail. Run the aggregate unit tests for the state machine, caps, lock/reopen and table close.

**Acceptance Scenarios**:

1. **Given** a competition, **When** a state change is requested, **Then** the competition itself decides whether the transition is allowed and raises the same module error as today when it is not.
2. **Given** an evaluation, **When** scores are submitted, adjusted or corrected, **Then** the evaluation enforces the caps, the total and the lock/reopen rules itself and raises the same errors as today.
3. **Given** a tasting table, **When** closing is requested, **Then** the table checks its close preconditions itself (open discrepancies, pending sheets) and raises the same errors as today.
4. **Given** any read slice, **When** it runs, **Then** it reads without change tracking and projects straight to its response shape.

---

### User Story 27 - The outside world sees nothing new (Priority: P1)

Clients, tests and the database see no change: same routes, status codes, error types, payload
shapes, realtime messages, emails and schema. The existing automated suites pass with their
assertions untouched.

**Why this priority**: A refactor that changes behaviour is a defect; this story is the safety net for
the other three.

**Independent Test**: Run every existing suite (unit, integration/contract, end-to-end) and check it
passes with no assertion edits; start on an empty database and on an existing local database and check
no migration is added, renamed or re-applied.

**Acceptance Scenarios**:

1. **Given** the existing contract tests (blind-anonymity absence tests included), **When** they run against the reorganized backend, **Then** they pass without edits to their assertions.
2. **Given** an empty database, **When** the application starts, **Then** every existing migration applies under its existing id and the schema equals the one before the change.
3. **Given** an existing local database already up to date, **When** the application starts, **Then** no migration runs and the migration histories are unchanged.

### Edge Cases

- A migration or model snapshot moves to a module's own project: its id stays the same, so each context's migration history still matches and nothing re-applies (FR-108).
- Integration tests start the application through its entry point: they keep working after the entry point moves to the host part (FR-110).
- Tests need module internals (handlers, aggregates, context): each module part grants access to the test projects only, not to other modules (FR-110).
- A helper used by two modules today (e.g. a lookup in one module used by another): it moves behind a contract in the shared contracts place or is duplicated in each module; it never creates a module-to-module dependency (FR-102).
- Background jobs, integration event handlers and realtime emits registered by a module keep being registered by that module, and run as today (FR-106).
- A module not yet moved during delivery keeps working from the current backend project until its turn (FR-111).
- Runtime discovery that relies on scanning one assembly (request handlers, validators, endpoint mapping, outbox/inbox handlers, entity configurations): it covers every module part after the split, so no handler, validator or route silently disappears (FR-106, FR-107).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-102**: Each business module (Catalog, Organization, Judging, Publication) MUST be its own build unit. No module build unit MAY depend on another module build unit; a reference from one module to another module's internal type MUST fail the build.
- **FR-103**: Types that a module exposes to other modules (contract interfaces, snapshots, integration events) MUST live in one shared contracts build unit, grouped per owning module. The shared contracts build unit and the shared kernel (entity base, timestamp marker, base data context, emit-after-commit) MUST NOT depend on any module.
- **FR-104**: Every slice MUST live in `<Module>/<Area>/<Slice>/` and consist of `Endpoint` (route only; maps the HTTP request to the request and sends it), `Request` (the MediatR command or query plus its inline validator), `Handler` (the request handler) and `Response` (only when the slice returns a body). File and type names MUST NOT repeat the slice name beyond the folder and namespace. Each area MUST have exactly one `Setup` that maps its route group; helpers shared inside an area MUST live in `<Area>/Shared/`.
- **FR-105**: Write handlers MUST change state only by loading the owning aggregate, calling its method and saving. The competition state machine, the score caps and total, the sheet lock and reopen rules, and the table close preconditions MUST be methods of their aggregates that raise the same module errors as today. Read handlers MUST read without change tracking and project to their response; they MUST NOT load an aggregate for change.
- **FR-106**: Each module MUST expose one module shape with exactly two entry points: registering its services (given the service collection and configuration) and mapping its endpoints. The host MUST list the modules explicitly in one place; modules MUST NOT be loaded from configuration.
- **FR-107**: MediatR MUST stay on the pinned 12.x line, in-module requests MUST keep going through it, and validation MUST keep running in the existing validation pipeline behaviour, now covering the requests and validators of every module build unit.
- **FR-108**: The REST API (routes, methods, status codes, headers), the realtime payloads, the error catalog (`urn:birrapoint:*` types) and every DTO shape MUST stay byte-for-byte as today. The database schema MUST stay as today: no migration is added, and every existing migration keeps its id so migration histories stay valid on fresh and existing databases.
- **FR-109**: Automated architecture tests MUST fail when: a module build unit depends on another module build unit; a slice breaks the FR-104 layout or naming; a read handler uses change tracking or loads an aggregate for change; a write handler changes aggregate state outside the aggregate's methods (aggregates expose no public setters for state they protect).
- **FR-110**: The host build unit (`BirraPoint.Host`, renamed from `BirraPoint.Api`; container build, CI/release workflows, PR labeler and Aspire reference follow the rename) MUST own the application entry point, authorization policies, error and rate-limit handling, the realtime hub, the job queue and audit data contexts and the module list. The existing unit and integration test projects MUST stay (no per-module test project), MUST keep starting the application through the entry point and MUST reach module internals only through access granted to them explicitly.
- **FR-111**: Delivery MUST be incremental: shared kernel, shared contracts and module shape first; then Catalog, Publication, Judging, Organization, one at a time; then host cleanup and docs. Each step MUST be its own pull request, opened after the previous one merges (spec, plan and tasks with the first; the last one closes the issue). Each step MUST leave the main line building and green, touch one module only, and put pure file moves (no logic change) in their own commit before any commit that changes code.
- **FR-112**: A module reference document MUST give, per module: routes and the policy that guards them, owned tables and schema, contracts exposed and consumed, integration events published and handled, and background jobs. The living architecture document MUST link to it instead of repeating it. The decision MUST be recorded as an architecture decision that supersedes ADR-0031 and the layout parts of ADR-0033/0034, and the project instructions (layout, backend conventions), the agents' scope notes and the namespace-move script MUST match the new layout.

### Key Entities

- **Module build unit**: one per business module; holds its domain (aggregates, errors), its data context and migrations, its areas and slices, and its integration code (contract implementations, integration event handlers). Depends only on the shared kernel, the shared contracts and building blocks.
- **Shared contracts build unit**: the only place for cross-module types, one group per owning module.
- **Shared kernel build unit**: entity base, timestamp marker, base data context, emit-after-commit; no business types.
- **Host build unit**: the single deployable application that composes the modules.
- **Area**: a business capability inside a module (e.g. Competitions, Import, Judges, Tables, Evaluations), with one setup and its slices.
- **Slice**: one use case (one endpoint), with the four predictable files.
- **Aggregate**: the entity that owns an invariant (Competition, Evaluation, TastingTable, …) and exposes the methods that change its state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-029**: 4 module build units, each with 0 dependencies on another module build unit; a deliberate cross-module reference makes the build fail (checked once per module).
- **SC-030**: 100% of slices follow the FR-104 layout; 0 occurrences of nested `Features/*/Features/`, `CommonComponents/`, `Base<X>Endpoints` or `<M>ModuleEndpoints` in the backend source.
- **SC-031**: 100% of existing unit, integration/contract and end-to-end tests pass with 0 edits to their assertions; only namespaces, references and test setup change.
- **SC-032**: 0 new migrations and 0 changed migration ids; a start on an existing up-to-date local database runs 0 migrations.
- **SC-033**: Every slice path is at most 4 folders below the module root (`<Module>/<Area>/<Slice>/<file>`), against 6 today.
- **SC-034**: Startup time and the existing latency budgets (p95 reads < 200 ms, writes < 500 ms) hold within the same limits as before the change.

## Assumptions

- Pure refactor: no product requirement, endpoint, payload, error or schema changes; the FR/SC ids above are structural rules so code and tests can cite them.
- MediatR 12.x, FluentValidation, EF Core and every other pinned package stay; splitting into several build units adds no new dependency, so no constitution amendment is needed (Principle II already allows a modular monolith with one deployable).
- Not adopted from the reference: message broker and inter-module request client (the database outbox of ADR-0042 stays), split deployment and configuration-driven module loading, four layers per module, the result monad (per-module exceptions of ADR-0038 stay), versioned frozen domain events, and API version folders.
- Not touched: frontend, Terraform, building blocks other than the validation one, and the behaviour of any feature. The only CI edits are the path and glob updates the host rename needs.
- Existing local databases need no reset: migration ids are kept, only the code that holds them moves.
