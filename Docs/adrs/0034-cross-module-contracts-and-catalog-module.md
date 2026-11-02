# 0034 - Cross-module contracts and the Catalog module

**Status:** Accepted
**Date:** 2026-11-02

## Context
ADR-0033 phase 1 moved the features into modules but left direct entity access across them, so the split was organisational only. Judging read `Competition`/`BeerEntry`/`Judge`, Publication read Judging evaluations, and Organization both read Judging tables (finalize guard, entries list) and enqueued the `GeneratePdfs` job after commit, so a crash between commit and enqueue lost the dispatch. The BJCP catalog (global, read-only reference data) lived inside Organization although nothing in it depends on competitions.

## Decision
**Catalog becomes the fourth module** (`BjcpStyle`, `GetStyles`, `GetStyleDetails`, `bjcp-2021.json`, seeding). It has no dependencies. Allowed direction: Catalog <- Organization <- Judging <- Publication, plus Publication -> Organization.

**Contracts.** `Modules/<M>/Contracts/` is the only surface another module may reference: interfaces, DTO records and notifications, never EF entities. Implementations are `internal sealed` classes in `Modules/<M>/Integration/`, registered by `Add<M>Module`.

| Contract | Owner (defines) | Implemented by | Used by |
|---|---|---|---|
| `IStyleCatalog` | Catalog | Catalog | Organization (Import, categories, entries list), Judging (table and judge sample projections), Publication (score-sheet PDFs) |
| `IConflictOfInterestChecker` | Organization | Organization | Judging (table assignment) |
| `IJudgeDirectory` | Organization | Organization | Judging (`IStoredEvaluations` judge names) |
| `IStoredEvaluations` | Judging | Judging | Publication (score-sheet PDFs), Judging Monitoring (entry evaluations) |
| `ICompetitionFinalizationGuard` | Organization | Judging | Organization |
| `IEntryTableAssignments` | Organization | Judging | Organization |
| `CompetitionFinalized` (notification) | Organization | published by Organization | Publication |

Provider-owned contracts cover downstream-needs-upstream data. When an upstream module needs downstream data (open tables on finalize, entry-to-table assignments), Organization defines the port and Judging implements it. This avoids a cycle.

**Queries are synchronous in-process interfaces.** Rejected: MediatR requests across modules (hidden coupling, no typed surface), read-only cross-module `DbSet` access (keeps the shared-schema coupling), UI composition (breaks existing API shapes), outbox/async events (eventual consistency; not needed in one process).

**Finalization is atomic.** `ChangeCompetitionStateCommand` publishes the in-process MediatR notification `CompetitionFinalized` before `SaveChanges`. Publication's handler stages `GeneratePdfs` with `IDispatchJobQueue.Stage` in the same transaction; the worker wake-up signal is sent after commit. This closes the enqueue-after-commit gap. Every state transition runs in a transaction that loads the competition with `SELECT … FOR UPDATE`, so concurrent requests serialize: one wins, the rest get `409 invalid-state-transition`, and at most one `GeneratePdfs` job is staged.

**Enforcement.** `UnitTests/Architecture/ModuleBoundaryTests.cs` scans sources: a module may use another module only through its `Contracts` namespace and may not touch its `DbSet`s. An exact, shrink-only allow-list holds 29 Judging/Publication -> Organization entries (phase-3 debt); a stale entry fails the build. Allowed without listing: `Modules/<M>/Persistence/` FK configurations (shared `AppDbContext`). Permanent kernel exceptions: `Common/Auth` resolvers (Keycloak subject -> organizer/judge), `Realtime/` hub authorization, `Common/Persistence/` (model composition, including the `DispatchJob` -> `Competition` FK).

## Consequences
- Cross-module data needs go through a typed, reviewable surface; the blind-anonymity boundary stays in Judging.
- Same-transaction semantics are kept (one process, one `AppDbContext`); no migration or API change.
- Data from two modules is combined in memory. The catalog is about 120 rows, so this is cheap; larger sets need a join-capable contract.
- COI co-owner lookup is now case-insensitive.
- 29 allow-list entries remain; ADR-0033 phase 3 removes them with synchronous contracts (one Organization table per step) and one in-process event for the BOS flag.
- Builds on ADR-0033 and ADR-0031; no constitution amendment.
