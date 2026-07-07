<!--
Sync Impact Report
==================
Version change: 1.1.0 → 1.2.0 (MINOR — local orchestration and deployment stack amended)
Modified principles: none
Modified sections:
  - Technology & Architecture Constraints:
    * Local orchestration: Docker Compose → .NET Aspire (AppHost + ServiceDefaults with
      OpenTelemetry, health checks, resilience defaults). Source: spec 001 Clarifications
      Session 2026-07-07 (Q1) and FR-044/FR-048.
    * Added Containerization constraint (multi-stage Docker images, no baked secrets) per FR-043.
    * Added Deployment constraint: Bicep + Azure Developer CLI (azd up) → Azure Container Apps;
      ACR; frontend Nginx image; Keycloak as container app in the same environment; PostgreSQL
      as in-environment container with persistent storage + scheduled backup/export per
      FR-045–FR-047 and Clarifications Q2/Q3.
Added sections: none
Removed sections: none
Templates:
  - .specify/templates/plan-template.md ✅ compatible (no orchestration references)
  - .specify/templates/spec-template.md ✅ compatible
  - .specify/templates/tasks-template.md ✅ compatible
  - Docs/01-Definicion-Tecnologica.md ✅ updated (§5 Infraestructura rewritten to match)
  - CLAUDE.md ✅ compatible (no orchestration references)
  - specs/001-birrapoint-mvp/plan.md, research.md, tasks.md, quickstart.md ✅ updated 2026-07-07
    (plan structure/context, research R-16–R-19, tasks T005 + new Phase 16 T095–T099,
    quickstart Aspire/azd commands).
Follow-up TODOs: none
Previous report (v1.1.0, 2026-07-06): backend stack .NET 8/9 → .NET 10 (LTS).
Previous report (v1.0.0, 2026-07-06): initial ratification of Core Principles I–X,
Technology & Architecture Constraints, Development Workflow & Quality Gates, Governance;
tasks-template test tasks made mandatory per Principle III.
-->

# BirraPoint Constitution

## Core Principles

### I. Spec-Driven Development (NON-NEGOTIABLE)

No implementation without an approved specification, plan, and task list. Every feature MUST
progress through the Spec Kit pipeline — `spec.md` → `plan.md` → `tasks.md` — and each artifact
MUST be reviewed and approved before the next stage begins. Code changes that introduce new
functionality without a corresponding approved task are rejected. Requirements changes discovered
mid-implementation MUST flow back into the spec first; the spec is never silently overridden by
code.

**Rationale**: The specs under `specs/**` are the single source of truth. Deciding behavior in
code review or ad hoc during implementation destroys traceability and makes AI-assisted
development unsafe.

### II. Modular Monolith with Vertical Slices

The backend is a single deployable ASP.NET Core application organized as a modular monolith. Each
feature is a vertical slice (request + handler + validation + persistence mapping) owning its own
DTOs and logic end to end. Slices MUST NOT reach into another slice's internals; cross-slice
interaction happens only through MediatR messages or explicitly shared contracts. Shared code
lives in a deliberately small shared kernel (cross-cutting infrastructure, domain primitives) —
anything else stays inside its slice. The Angular frontend mirrors this with Feature-Sliced
Design: code is grouped by business feature, not by file type.

**Rationale**: Slice isolation keeps the monolith maintainable and independently testable, and
preserves the option to extract modules later without a rewrite.

### III. Test-First Development (NON-NEGOTIABLE)

TDD is mandatory: tests are written first, verified to fail, then implementation makes them pass
(red → green → refactor). Every slice MUST have handler-level unit tests covering validation and
business rules; every API endpoint MUST have a contract/integration test. Test tasks in `tasks.md`
are never optional and always precede their implementation tasks. Work is not complete until unit
tests, applicable integration tests, lint/type checks, and the build all pass.

**Rationale**: Tests written after the fact validate what was built, not what was specified.
Test-first keeps the acceptance criteria from the spec executable.

### IV. Simplicity and Design Discipline (KISS, DRY, SOLID)

Prefer the simplest design that satisfies the approved spec (KISS, YAGNI); speculative
abstractions and configuration for imagined futures are rejected. Knowledge MUST NOT be duplicated
(DRY): validation rules, scoring limits, and domain constants live in exactly one place per layer.
SOLID applies pragmatically — small units with a single reason to change, dependencies on
abstractions at slice boundaries — never as ceremony. Any complexity that violates these rules
MUST be justified in the plan's Complexity Tracking table.

**Rationale**: A small team with AI agents ships faster and safer when every piece of code has one
obvious home and no clever indirection.

### V. Minimal, Justified Dependencies

Every new third-party dependency MUST be justified in the plan: what it provides, why the
framework or standard library cannot do it, and its maintenance/health status. Prefer capabilities
already in the approved stack (Angular, .NET, EF Core, SignalR, Keycloak, PostgreSQL, Tailwind,
Dexie.js, MediatR, FluentValidation) before adding anything. Micro-dependencies that save only a
few lines are rejected.

**Rationale**: Each dependency is a permanent security, upgrade, and cognitive cost; the offline
PWA also pays for every kilobyte shipped.

### VI. Clear API Contracts

APIs are contract-first: REST endpoints and SignalR hub events MUST be defined in
`specs/[feature]/contracts/` before implementation and exposed via OpenAPI. Contracts specify
request/response shapes, validation rules, auth requirements, and error responses. Errors MUST use
RFC 7807 Problem Details consistently. Breaking an existing contract requires an explicit spec
amendment and versioning decision — never a silent change the frontend discovers at runtime.

**Rationale**: Frontend, backend, and offline sync are developed against the same contract;
ambiguity there multiplies into every layer, including queued offline payloads.

### VII. Security by Default

Authentication and identity are delegated exclusively to Keycloak (OIDC, Authorization Code +
PKCE) — no custom login, password storage, or token issuance. Every API endpoint requires
authentication and role authorization (`ORGANIZER`, `JUDGE`) by default; anonymous access is an
explicit, justified exception. All input is validated at the API boundary (FluentValidation)
before touching business logic or the database; data access goes through EF Core with
parameterized queries only. Secrets never enter the repository — configuration comes from
environment variables or secret stores. Sensitive data is never logged. Blind-tasting anonymity is
a security invariant: no payload sent to a judge may ever contain brewer or entrant identity.

**Rationale**: The platform handles personal data and competition integrity; a single leaked
brewer identity compromises an entire competition.

### VIII. Accessibility by Default

The PWA MUST meet WCAG 2.1 AA. Every interactive flow MUST be keyboard-navigable and
screen-reader compatible; forms — especially the judge evaluation sheet — MUST have programmatic
labels, visible focus, and error messages announced to assistive technology. Color contrast MUST
satisfy AA ratios, keeping in mind judges work on mobile devices in variable venue lighting.
Drag-and-drop interactions (table ordering) MUST have an accessible alternative. Accessibility
acceptance criteria are part of each feature spec, and accessibility checks are part of each
story's definition of done.

**Rationale**: Judges are conscripted users on their own devices; the product fails if any judge
cannot complete an evaluation.

### IX. Performance Budgets

Every feature plan MUST declare its performance budget in the spec's Success Criteria. Project
defaults, applying unless a plan justifies otherwise: API reads p95 < 200 ms and writes p95
< 500 ms under expected competition load; initial PWA load interactive in < 3 s on a mid-range
mobile device over 4G; initial JS bundle ≤ 500 KB gzipped; SignalR dashboard updates visible
< 1 s after a judge submits; offline sheet save < 100 ms. Budgets are verified before a story is
considered done; regressions block merge.

**Rationale**: Live events cannot pause for a slow dashboard, and judge devices at a venue cannot
be assumed to be fast or well-connected.

### X. Documentation as Part of Done

A task is not complete until its documentation is updated in the same change: API/contract docs
for endpoint changes, `quickstart.md` for setup changes, `Docs/` for product-behavior changes, and
agent guidance (`CLAUDE.md`) when commands or structure change. Architecture decisions with
non-obvious trade-offs MUST record the reasoning (in the plan or an ADR). Spec artifacts are
committed together with the implementation they describe.

**Rationale**: Undocumented behavior silently forks the source of truth that Principle I depends
on — for humans and AI agents alike.

## Technology & Architecture Constraints

The approved stack is defined in `Docs/01-Definicion-Tecnologica.md` and is binding for the MVP:

- **Frontend**: Angular 17+ (standalone components, Signals), Feature-Sliced Design, PWA via
  `@angular/pwa`, Dexie.js (IndexedDB) for offline-first storage, Tailwind CSS (mobile-first).
- **Backend**: .NET 10 (LTS) ASP.NET Core Minimal APIs, Vertical Slice Architecture with MediatR,
  FluentValidation in the MediatR pipeline, SignalR (`CompetitionHub`) for real-time updates.
- **Identity**: Keycloak — OIDC Authorization Code + PKCE; backend verifies JWTs and authorizes
  via claims. Roles: `ORGANIZER`, `JUDGE`.
- **Persistence**: PostgreSQL via EF Core (Npgsql), code-first migrations.
- **Local orchestration**: .NET Aspire — a single AppHost project orchestrates PostgreSQL,
  Keycloak, the backend API, the frontend, and the mail sink with one command; a ServiceDefaults
  project injects OpenTelemetry, health checks, and resilience defaults into every service.
- **Containerization**: every runtime component ships as a multi-stage Docker image (backend:
  .NET SDK build → ASP.NET runtime; frontend: Node build → Nginx Alpine serving static files);
  images MUST NOT contain secrets or environment-specific configuration.
- **Deployment**: Azure Container Apps, provisioned declaratively with Bicep through the Azure
  Developer CLI — `azd up` builds images, pushes to Azure Container Registry, and deploys in a
  single command. Production topology: one ACA environment hosting the frontend (public
  ingress), the backend API, Keycloak, and PostgreSQL as an in-environment container with
  persistent storage, scheduled backup/export, and a documented restore procedure.

Deviations from this stack require a constitution amendment, not a per-feature decision.
Offline-first is an architectural property, not a feature: judge-facing evaluation flows MUST
function without connectivity and sync silently on reconnection.

## Development Workflow & Quality Gates

- Feature flow: `/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` →
  task review → `/speckit-implement`. The plan's Constitution Check gate MUST pass before design
  and be re-checked after it.
- Work happens on feature branches with small, reviewable commits; spec artifacts are committed
  with the implementation.
- Definition of done for every story: tests written first and passing, lint/type checks and build
  green, accessibility checks passed, performance budget respected, documentation updated.
- Code review verifies constitutional compliance; unjustified violations block merge. Justified
  exceptions live in the plan's Complexity Tracking table.

## Governance

This constitution supersedes all other development practices in this repository. Where CLAUDE.md,
templates, or prior docs conflict with it, the constitution wins and the conflicting artifact MUST
be updated.

- **Amendments**: proposed via pull request that states the change, its rationale, and its
  migration impact; the Sync Impact Report at the top of this file is updated and dependent
  templates are propagated in the same change.
- **Versioning**: semantic versioning. MAJOR — principle removals or incompatible redefinitions;
  MINOR — new principles or materially expanded guidance; PATCH — clarifications and wording.
- **Compliance review**: every plan runs the Constitution Check gate; every PR review confirms the
  Definition of Done above. Runtime development guidance for agents lives in `CLAUDE.md` and must
  stay consistent with this document.

**Version**: 1.2.0 | **Ratified**: 2026-07-06 | **Last Amended**: 2026-07-07
