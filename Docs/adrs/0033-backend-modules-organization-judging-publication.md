# 0033 - Backend modules: Catalog, Organization, Judging, Publication

**Status:** Accepted (phase 2 detailed in ADR-0034; Catalog became a fourth module there)
**Date:** 2026-11-01

## Context
Constitution Principle II mandates a modular monolith, but the backend is one flat set of nine feature folders (ADR-0031) over a shared `Api/Domain/` holding every entity. Any slice can read any entity, so nothing prevents coupling between preparation (competitions, catalog, imports, judges), tasting (tables, order, evaluations) and publication (PDFs, ZIP, emails). The blind-tasting boundary also has no structural home: Judging must not see entrant data, while Publication may, because it runs after tasting ends.

## Decision
Group the features into modules (three at first; Catalog was split off in phase 2, ADR-0034) under `backend/src/BirraPoint.Api/Modules/<Module>/`, each with `Domain/`, `Persistence/` (EF configurations), `Features/<Feature>/` (ADR-0031 layout unchanged) and `<Module>ModuleEndpoints.cs` (`Map<Module>Module`). Namespaces equal folders.

| Module | Features | Entities |
|---|---|---|
| **Catalog** (global read-only BJCP reference data, no dependencies) | Catalog | `BjcpStyle` |
| **Organization** (everything before tasting; identities, master data) | Competitions, Import (beers + judge roster, ADR-0032), Judges | `Organizer`, `Competition`, `CompetitionCategory`, `CompetitionCategoryStyle`, `Participant`, `BeerEntry`, `EntryCollaborator`, `Judge`, `Invitation`, import staging entities |
| **Judging** (tasting core) | Tables, TastingOrder, Evaluations, Monitoring | `TastingTable`, `TableJudge`, `TableSample`, `Evaluation`, `DiscrepancyAlert` |
| **Publication** (post-tasting; async, CPU-heavy) | Dispatch | `GeneratedScoreSheet`, `ResultsArchive` |

`Api/Domain/` shrinks to the shared kernel: `Entity` (+ `ITimestamped`, declared in `Entity.cs`), `AuditLog`, `DispatchJob` (+ status/type). `Api/Common/` (`AppDbContext`, migrations, auth, jobs, errors, behaviors, audit) and `Api/Realtime/` stay. Tests mirror the layout (`Modules/<M>/<Feature>/`). A reflection-based `UnitTests/Architecture/ModuleLayoutTests.cs` enforces namespaces and ownership.

Persistence stays a single `AppDbContext` over the same tables: no migration, only the model snapshot CLR names change.

Delivery in three phases:

1. **Phase 1 (done)**: pure move. Cross-module entity access is unchanged.
2. **Phase 2 (done, ADR-0034)**: Catalog module, `Contracts/` + `Integration/` per module, atomic finalization, and `ModuleBoundaryTests` forbidding cross-module access with a shrink-only allow-list.
3. **Phase 3 (open, revised 2026-11-02)**: synchronous contracts only, one Organization table per step — `ICompetitionAccess`, extended `IJudgeDirectory`, `IEntryDirectory` and `IResultRecipients` — until the module allow-list is empty. The one cross-module write (`NotValidForBos` set by Judging) becomes an in-process event, `EntryJudgeAssignmentsChanged`, handled by Organization in the same transaction (ADR-0034 conventions). No behaviour change, so no spec amendment.
4. **Follow-ups (behaviour changes, spec amendment first)**: `JudgeEmailChanged` (re-check COI/BOS after an e-mail correction) and `EvaluationCorrected` (stale PDFs after a post-finalize correction).

Dropped from phase 3: Judging-local copies of samples and judges, their backfill migration, and events without a consumer (`EntriesConsolidated`, `JudgeRegistered`, `TableClosed`, a generic `CompetitionStateChanged`). With one process and one `AppDbContext` they add eventual consistency and sync code without a current need. Revisit only if a module is split into its own service.

Publication may read Judging evaluations and Organization participant data because it runs after tasting ends, when anonymity no longer applies.

## Consequences
- Ownership is explicit and reviewable; the blind-anonymity boundary maps to the Judging module.
- After phase 2, modules reference each other only through `Contracts/`, except the allow-listed Judging/Publication -> Organization reads that phase 3 removes with synchronous contracts.
- Rejected: per-module `DbContext`s or schemas. They would force irreversible production schema moves for little benefit at this size. They stay possible later behind the phase 2/3 contracts.
- Rejected: doing all three phases at once. A pure move is reviewable and low risk; contracts deserve separate review, and each phase-3 step removes one group of allow-list entries.
- Old migration `*.Designer.cs` files keep the old CLR names. This is harmless because only the latest snapshot is used.
- Frontend is unchanged (already FSD). Builds on ADR-0031 and ADR-0032; no constitution amendment.
- Kernel → Organization references are allowed for now: auth identity resolution (`Common/Auth/{ICurrentUser,CurrentUser,JudgeResolver,OrganizerResolver}.cs` use `Organizer`/`Judge`) and the `DispatchJob` → `Competition` FK (`Common/Persistence/Configurations/DispatchJobConfiguration.cs`). ADR-0034 keeps these as permanent, documented exceptions in the architecture test.
- Naming: the third module is `Publication`, not `Results`, because the namespace `BirraPoint.Api.Modules.Results` shadowed `Microsoft.AspNetCore.Http.Results` in every module endpoint file. Its extension method is `MapPublicationModule`.
- `<Module>ModuleEndpoints.cs` files are `internal`. The architecture test allows `Entity`, `ITimestamped`, `AuditLog` and `DispatchJob` (+ status/type) in the shared kernel `Api/Domain/`.
