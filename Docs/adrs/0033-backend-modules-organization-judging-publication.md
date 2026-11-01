# 0033 - Backend modules: Organization, Judging, Publication

**Status:** Accepted
**Date:** 2026-11-01

## Context
Constitution Principle II mandates a modular monolith, but the backend is one flat set of nine feature folders (ADR-0031) over a shared `Api/Domain/` holding every entity. Any slice can read any entity, so nothing prevents coupling between preparation (competitions, catalog, imports, judges), tasting (tables, order, evaluations) and publication (PDFs, ZIP, emails). The blind-tasting boundary also has no structural home: Judging must not see entrant data, while Results may, because it runs after tasting ends.

## Decision
Group the features into three modules under `backend/src/BirraPoint.Api/Modules/<Module>/`, each with `Domain/`, `Persistence/` (EF configurations), `Features/<Feature>/` (ADR-0031 layout unchanged) and `<Module>ModuleEndpoints.cs` (`Map<Module>Module`). Namespaces equal folders.

| Module | Features | Entities |
|---|---|---|
| **Organization** (everything before tasting; identities, master data) | Catalog, Competitions, Import (beers + judge roster, ADR-0032), Judges | `Organizer`, `Competition`, `CompetitionCategory`, `CompetitionCategoryStyle`, `BjcpStyle`, `Participant`, `BeerEntry`, `EntryCollaborator`, `Judge`, `Invitation`, import staging entities |
| **Judging** (tasting core) | Tables, TastingOrder, Evaluations, Monitoring | `TastingTable`, `TableJudge`, `TableSample`, `Evaluation`, `DiscrepancyAlert` |
| **Publication** (post-tasting; async, CPU-heavy) | Dispatch | `GeneratedScoreSheet`, `ResultsArchive` |

`Api/Domain/` shrinks to the shared kernel: `Entity` (+ `ITimestamped`, declared in `Entity.cs`), `AuditLog`, `DispatchJob` (+ status/type). `Api/Common/` (`AppDbContext`, migrations, auth, jobs, errors, behaviors, audit) and `Api/Realtime/` stay. Tests mirror the layout (`Modules/<M>/<Feature>/`). A reflection-based `UnitTests/Architecture/ModuleLayoutTests.cs` enforces namespaces and ownership.

Persistence stays a single `AppDbContext` over the same tables: no migration, only the model snapshot CLR names change.

Delivery in three phases:

1. **Phase 1 (T151)**: pure move. Cross-module entity access is unchanged.
2. **Phase 2 (T152)**: cross-module contracts (`IConflictOfInterestChecker` from Organization for `CoiDetector` and TastingOrder; a finalized-evaluations query from Judging for Publication) replace direct entity access. The architecture test then forbids it.
3. **Phase 3 (T153)**: events (Organization: `CompetitionStateChanged`, `EntriesConsolidated`, `JudgeRegistered`; Judging: `TableClosed`, `EvaluationCorrected`) and Judging-local copies of samples and judges. Needs a spec amendment and its own ADR first.

Publication may read Judging evaluations and Organization participant data because it runs after tasting ends, when anonymity no longer applies.

## Consequences
- Ownership is explicit and reviewable; the blind-anonymity boundary maps to the Judging module.
- Until phase 2, modules still read each other's entities directly, so the split is organisational only.
- Rejected: per-module `DbContext`s or schemas. They would force irreversible production schema moves for little benefit at this size. They stay possible later behind the phase 2/3 contracts.
- Rejected: doing all three phases at once. A pure move is reviewable and low risk; contracts and eventual consistency (backfill migration) deserve separate review.
- Old migration `*.Designer.cs` files keep the old CLR names. This is harmless because only the latest snapshot is used.
- Frontend is unchanged (already FSD). Builds on ADR-0031 and ADR-0032; no constitution amendment.
- Naming: the third module is `Publication`, not `Results`, because the namespace `BirraPoint.Api.Modules.Results` shadowed `Microsoft.AspNetCore.Http.Results` in every module endpoint file. Its extension method is `MapPublicationModule`.
- `<Module>ModuleEndpoints.cs` files are `internal`. The architecture test allows `Entity`, `ITimestamped`, `AuditLog` and `DispatchJob` (+ status/type) in the shared kernel `Api/Domain/`.
