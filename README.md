# BirraPoint

A PWA for running homebrew/craft beer competitions with blind tastings (*catas a ciegas*):
organizer provisioning (competition wizard, `.xlsx` entry import validated against the BJCP 2021
style catalog, bulk judge invitations, tasting tables with conflict-of-interest protection), an
offline-first judge evaluation flow with a shared fixed tasting order and BJCP score caps,
discrepancy consensus between judges, a real-time organizer dashboard, and immutable closing with
automated PDF/ZIP/email results dispatch.

Best of Show and tie-breaks are out of scope for this release — only a `NotValidForBos` flag is
recorded for later use.

## Stack

- **Backend**: .NET 10 / C# 14, ASP.NET Core Minimal APIs, MediatR, EF Core + PostgreSQL 16,
  SignalR, .NET Aspire (local orchestration + cloud deployment model).
- **Frontend**: Angular 20 (standalone components + Signals), a PWA with an offline-first Dexie.js
  (IndexedDB) engine, Tailwind CSS.
- **Identity**: Keycloak (OIDC, Authorization Code + PKCE) — the app never implements its own
  login/password/token handling.
- **Testing**: xUnit + Testcontainers (real PostgreSQL, no in-memory DB) on the backend; Jest +
  Playwright + axe-core (accessibility) + k6 (performance) on the frontend.

## Running locally

Prerequisites: Docker Desktop running, .NET 10 SDK, Node.js 24+.

```bash
dotnet run --project backend/src/BirraPoint.AppHost
```

One command brings up the full local topology — PostgreSQL, Keycloak (realm auto-imported), a
Mailpit inbox for outgoing email, the API, and the Angular frontend — orchestrated by .NET Aspire.
The Aspire dashboard URL (with its login link) is printed on startup; it links out to every other
service's local endpoint, including the frontend at `http://localhost:4200`.

For frontend-only iteration against an already-running backend: `cd frontend && npm ci && npm start`.

## Documentation

This repository is developed spec-first (GitHub Spec Kit) — the specification, not the code, is
the primary source of truth:

- [`CLAUDE.md`](./CLAUDE.md) — full development guide: commands, conventions, architecture,
  non-negotiable invariants, and the mandatory per-task implementation workflow.
- [`specs/001-birrapoint-mvp/`](./specs/001-birrapoint-mvp/) — the feature specification
  (`spec.md`), technical plan (`plan.md`), task breakdown (`tasks.md`), and supporting design
  artifacts (data model, API/SignalR contracts, manual validation scenarios in `quickstart.md`).
- [`Docs/arquitectura_viva.md`](./Docs/arquitectura_viva.md) — living documentation of the actual
  current system state (English); [`Docs/`](./Docs/) also holds the original Spanish product
  definition, which the English spec supersedes.
- [`Docs/adrs/`](./Docs/adrs/) — architecture decision records for significant technical choices.
