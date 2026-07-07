# Implementation Plan: BirraPoint MVP — Beer Competition Blind-Tasting Platform

**Branch**: `001-birrapoint-mvp` | **Date**: 2026-07-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-birrapoint-mvp/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Build the BirraPoint MVP: a PWA for running beer competitions with blind tastings — organizer
provisioning (wizard, `.xlsx` entry import against the BJCP 2021 catalog, judge invitations,
table setup with conflict-of-interest protection), an offline-first judge evaluation flow with a
shared fixed tasting order and BJCP score caps, discrepancy consensus, a real-time organizer
dashboard, and immutable closing with automated PDF/ZIP/email dispatch.

Technical approach: Angular PWA (standalone components + Signals, Feature-Sliced Design, Dexie.js
offline queue, Tailwind) talking to a .NET 10 modular monolith (Minimal APIs, Vertical Slice
Architecture with MediatR + FluentValidation, SignalR `CompetitionHub`, EF Core/PostgreSQL),
identity delegated to Keycloak (OIDC Authorization Code + PKCE, roles `ORGANIZER`/`JUDGE`).
Background dispatch (QuestPDF PDFs, ZIP bundling, invitation/result emails) runs in-process via a
hosted worker over a DB-persisted job queue. Local development is orchestrated by .NET Aspire
(AppHost + ServiceDefaults); every component ships as a multi-stage Docker image and deploys to
Azure Container Apps via Bicep/`azd up` (constitution v1.2.0, FR-043–FR-048). Full details in
[research.md](./research.md).

## Technical Context

**Language/Version**: Backend: C# 14 on .NET 10 (LTS). Frontend: TypeScript 5.x on Angular 20
(satisfies the "17+" constraint; standalone components and Signals are the defaults).

**Primary Dependencies**:
- Backend: ASP.NET Core Minimal APIs, MediatR 12.x (last Apache-2.0 line), FluentValidation,
  EF Core + Npgsql, SignalR, ClosedXML (`.xlsx` parsing), QuestPDF Community (PDF generation),
  `System.IO.Compression` (ZIP), MailKit (SMTP), Keycloak Admin REST API (judge provisioning),
  .NET Aspire AppHost + ServiceDefaults (orchestration, OpenTelemetry, health checks, resilience).
- Frontend: `@angular/pwa` (service worker), Dexie.js (IndexedDB), Tailwind CSS,
  `@angular/cdk/drag-drop`, `keycloak-angular` + `keycloak-js` (OIDC PKCE),
  `@microsoft/signalr` client.

**Storage**: PostgreSQL 16 via EF Core code-first migrations (server of record); IndexedDB via
Dexie.js on judge devices (drafts + offline outbox only, never the source of truth).

**Testing**: Backend: xUnit; handler-level unit tests; contract/integration tests with
`WebApplicationFactory` + Testcontainers (PostgreSQL). Frontend: Jest (unit), Playwright (E2E,
including offline simulation and `axe-core` accessibility checks).

**Target Platform**: Backend: Linux containers — orchestrated locally by .NET Aspire, deployed to
Azure Container Apps (ACR + Bicep via `azd up`; Keycloak and PostgreSQL run as containers in the
same ACA environment, per Clarifications 2026-07-07). Frontend: multi-stage Node→Nginx image;
evergreen mobile/desktop browsers as an installable PWA; judge flow designed for mid-range
Android/iOS devices on flaky venue networks.

**Project Type**: Web application (frontend + backend + identity provider + database).

**Performance Goals**: API reads p95 < 200 ms, writes p95 < 500 ms at expected load; dashboard
reflects a completed evaluation < 1 s (SC-005); order-fix propagation < 1 s (SC-004); local draft
persistence < 300 ms (SC-003); initial JS bundle ≤ 500 KB gzipped; PWA interactive < 3 s on 4G.

**Constraints**: Offline-first judge evaluation flow (architectural property); WCAG 2.1 AA on all
judge-facing flows incl. keyboard alternative to drag & drop (SC-009); strict anonymity — judge
payloads must never contain entrant identity (BR-01/FR-019); immutability after table close
(FR-034); idempotent sync, at most one evaluation per judge+sample (FR-029); single-command local
start and cloud provision/deploy (FR-044/FR-045); health checks + telemetry on every service
(FR-048); in-environment PostgreSQL requires scheduled backup/export + documented restore (FR-047).

**Scale/Scope**: Single live event design center: ~500 entries, ~100 judges, ~25 tables;
12 user stories, 42 functional requirements; multi-competition data model from day one.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Spec-Driven Development | ✅ PASS | Approved spec + 5 recorded clarifications precede this plan; tasks follow via `/speckit-tasks`. |
| II | Modular Monolith / Vertical Slices | ✅ PASS | Single deployable API; one slice per feature under `Features/`; cross-slice interaction via MediatR only; small shared kernel (`Domain/`, `Common/`). Frontend mirrors with FSD. |
| III | Test-First Development | ✅ PASS | Contract tests per endpoint, handler unit tests per slice, E2E per user story; test tasks precede implementation tasks (enforced by tasks template). |
| IV | Simplicity & Design Discipline | ✅ PASS | No CQRS event sourcing, no microservices, no message broker; in-process job queue persisted in PostgreSQL is the simplest design that survives restarts. |
| V | Minimal, Justified Dependencies | ✅ PASS | Every dependency beyond the constitutional stack is justified in research.md §Dependencies (ClosedXML, QuestPDF, MailKit, keycloak-angular, @microsoft/signalr, Testcontainers, Jest, Playwright). |
| VI | Clear API Contracts | ✅ PASS | `contracts/rest-api.md`, `contracts/signalr-hub.md`, `contracts/import-file.md` written before implementation; RFC 7807 ProblemDetails everywhere; OpenAPI exposed at runtime. |
| VII | Security by Default | ✅ PASS | Keycloak OIDC + PKCE only; `RequireAuthorization()` fallback policy (deny-by-default), role policies per endpoint; FluentValidation at boundary; EF Core parameterized; secrets via env vars; anonymity enforced structurally — judge-facing DTOs physically lack entrant fields. |
| VIII | Accessibility by Default | ✅ PASS | CDK drag-drop paired with keyboard reorder controls (FR-020); axe-core checks in Playwright E2E; WCAG 2.1 AA acceptance per story. |
| IX | Performance Budgets | ✅ PASS | Budgets declared above from spec SC + constitutional defaults; verified in E2E/load checks before story completion. |
| X | Documentation as Part of Done | ✅ PASS | quickstart.md, contracts, data-model produced now; CLAUDE.md gains build/test commands when scaffolding lands (setup tasks). |

**Post-Phase-1 re-check (2026-07-06)**: ✅ PASS — design artifacts introduce no violations; no
Complexity Tracking entries required.

**Post-amendment re-check (2026-07-07, constitution v1.2.0)**: ✅ PASS — Aspire local
orchestration, containerization, and ACA/Bicep deployment constraints incorporated
(FR-043–FR-048, research R-16–R-19, tasks Phase 16); still no Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```text
specs/001-birrapoint-mvp/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── rest-api.md
│   ├── signalr-hub.md
│   └── import-file.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── BirraPoint.AppHost/             # .NET Aspire orchestration: PostgreSQL, Keycloak (realm
│   │                                   #   import), Mailpit, API, frontend; azd deployment model
│   ├── BirraPoint.ServiceDefaults/     # OpenTelemetry, health checks, resilience defaults
│   └── BirraPoint.Api/                 # Single deployable modular monolith
│       ├── Program.cs                  # Minimal API bootstrap, auth, SignalR, OpenAPI
│       ├── Dockerfile                  # Multi-stage: .NET SDK build → ASP.NET runtime
│       ├── Domain/                     # Shared kernel: entities, enums, invariants
│       ├── Common/                     # Cross-cutting: MediatR behaviors (validation),
│       │                               #   ProblemDetails mapping, auth policies,
│       │                               #   persistence (DbContext), audit, job queue
│       ├── Features/
│       │   ├── Competitions/           # Wizard CRUD + state transitions
│       │   ├── Catalog/                # BJCP 2021 styles (read-only, seeded)
│       │   ├── Import/                 # xlsx upload, row validation, correction, consolidation
│       │   ├── Judges/                 # Bulk registration, Keycloak provisioning, invitations
│       │   ├── Tables/                 # Table CRUD, COI validation, BOS flag, live removal
│       │   ├── TastingOrder/           # Fix order, sequence enforcement
│       │   ├── Evaluations/            # Submit (idempotent), discrepancy detection, locking
│       │   ├── Monitoring/             # Progress queries, audit read models, SignalR events
│       │   └── Dispatch/               # Close event, PDF/ZIP jobs, result emails
│       └── Realtime/                   # CompetitionHub + group management
└── tests/
    ├── BirraPoint.Api.UnitTests/       # Handler + validator tests per slice
    └── BirraPoint.Api.IntegrationTests/# Contract tests (WebApplicationFactory + Testcontainers)

frontend/
├── Dockerfile                          # Multi-stage: Node build → Nginx Alpine (static serve)
├── nginx.conf
├── src/
│   └── app/
│       ├── core/                       # Auth (Keycloak), API client, SignalR service,
│       │                               #   offline engine (Dexie: drafts + outbox), layout
│       ├── features/
│       │   ├── auth/                   # Login callback, role routing guards
│       │   ├── competition-wizard/     # Organizer: create/edit, drafts
│       │   ├── entry-import/           # Organizer: upload, mapping & correction screen
│       │   ├── judge-management/       # Organizer: bulk invite, delivery status
│       │   ├── table-management/       # Organizer: tables, assignment, COI errors
│       │   ├── judge-tables/           # Judge: my tables, blind sample list, order fixing
│       │   ├── evaluation-sheet/       # Judge: scored sections, offline drafts, submit
│       │   ├── discrepancy/            # Judge: alert & adjustment UI
│       │   ├── dashboard/              # Organizer: live progress, audit drill-down, removal
│       │   └── results-dispatch/       # Organizer: ZIP download, email statuses, retries
│       └── shared/                     # UI primitives, pipes, a11y helpers
└── tests/                              # Jest unit; e2e/ Playwright suites

azure.yaml                              # azd project definition (build → ACR → ACA, single azd up)
infra/
├── bicep/                              # ACA environment, ACR, container apps: frontend (public
│                                       #   ingress), backend, Keycloak; PostgreSQL container
│                                       #   with persistent storage
├── backup/                             # Scheduled pg_dump export job + documented restore
└── keycloak/birrapoint-realm.json      # Realm import (roles, clients, seeded organizer)
```

**Structure Decision**: Web application layout (backend + frontend at repo root, matching the
constitution's modular-monolith + FSD mandate). Backend slices live under
`backend/src/BirraPoint.Api/Features/`, one directory per business capability, with `Domain/` and
`Common/` as the deliberately small shared kernel. Frontend mirrors the same capabilities under
`frontend/src/app/features/` with shared infrastructure in `core/`. Local orchestration lives in
the Aspire `BirraPoint.AppHost` project; the cloud topology in `azure.yaml` + `infra/bicep/`
(deployed with `azd up`).

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None — no constitutional violations to justify.
