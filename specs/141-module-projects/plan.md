# Implementation Plan: One project per module with flat vertical slices

**Branch**: `feature/141-module-projects` (PR 1; later PRs `feature/141-<step>`) | **Date**: 2026-11-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/141-module-projects/spec.md` (US24–US27, FR-102–FR-112, SC-029–SC-034).

## Summary

Split `src/BirraPoint.Api` (about 330 `.cs` files, four modules) into `BirraPoint.Common` (shared kernel),
`BirraPoint.Contracts` (cross-module types), four module projects under `src/Modules/` and a thin
`BirraPoint.Host`. Slices become `<Area>/<Slice>/{Endpoint,Request,Handler,Response}.cs` with one `Setup.cs` per
area. Write rules (competition state machine, score caps, sheet lock/reopen, table close, tasting order) move onto their
aggregates; reads become `AsNoTracking` projections. Behaviour, wire contracts and schema stay the same. Delivered in six
sequential PRs (R-141-09). Decisions: [research.md](research.md).

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: unchanged — ASP.NET Core Minimal APIs, MediatR 12.5.x, FluentValidation, EF Core + Npgsql, SignalR, BuildingBlocks.*. No new package.
**Storage**: PostgreSQL 16, six contexts (ADR-0043); schema and migration ids unchanged (R-141-03).
**Testing**: xUnit unit tests (`tests/BirraPoint.Api.UnitTests`), integration/contract tests with `WebApplicationFactory<Program>` + Testcontainers (`tests/BirraPoint.Api.IntegrationTests`), Playwright E2E. Both test projects stay (clarified).
**Target Platform**: Linux container (ACA / ECS), local Aspire.
**Project Type**: web service (modular monolith, one deployable).
**Performance Goals**: unchanged budgets (p95 reads < 200 ms, writes < 500 ms); startup not slower (SC-034).
**Constraints**: byte-for-byte REST/SignalR/error catalog (FR-108); no migration; each PR one module and green `main` (FR-111).
**Scale/Scope**: 4 modules, 45 slices, ~50 `CommonComponents` files, 6 contexts, 9 migration files groups.

## Constitution Check

| Principle | Status |
|---|---|
| I. Spec-driven | ✅ spec approved (Stop 1), plan + tasks before code. |
| II. Modular monolith with vertical slices | ✅ strengthened: one deployable, slice isolation now compiler-enforced. No amendment needed. |
| III. Test-first | ✅ architecture tests for the new rules written first and failing (on fixtures and on the current tree); aggregate unit tests before moving logic; existing suites are the regression net and must pass unchanged in assertions. |
| Blind anonymity, state machine, idempotent sync, immutability, scoring | ✅ no behaviour change; contract tests (absence tests included) untouched; aggregates get unit tests that mirror the current rule tests. |
| Security (deny-by-default, policies) | ✅ route groups keep the same `RequireAuthorization` per area `Setup.cs`; integration auth tests unchanged. |
| Contract-first | ✅ no endpoint/event/entity change, so no living contract edit (`rest-api.md`, `signalr-hub.md`, `data-model.md` untouched — see [data-model.md](data-model.md)). |
| Pinned stack | ✅ no new dependency; MediatR 12.x; `BuildingBlocks.Validation.MediatR` gains a multi-assembly overload only. |

Re-check after design: no violation. No Complexity Tracking entries.

## Project Structure

### Documentation (this feature)

```text
specs/141-module-projects/
├── spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
└── checklists/requirements.md
```

### Source Code (target, after PR 6)

```text
src/
  BirraPoint.Host/                 Program.cs (module list), Auth/ RateLimiting/ Errors/ Secrets/, Realtime/ (CompetitionHub,
                                   EventPublisher, DispatchProgressObserver), Composition/ (CurrentUserAuditActor),
                                   Persistence/ (StartupMigrations, LegacyDatabaseGuard, DatabaseHealthCheck), Dockerfile
  BirraPoint.Common/               Domain/ (Entity, ITimestamped, DispatchJobType), Modules/ (IModule, ICommand, IQuery),
                                   Persistence/ (BirraPointDbContext, registration, SharedTransaction, interceptors, DbSchemas,
                                   LikePattern, DispatchJobQueries, Jobs/ + Audit/ contexts & migrations),
                                   Realtime/ (IEventPublisher, CompetitionGroups, CompetitionEvents), Security/ (policy names, Keycloak consts)
  BirraPoint.Contracts/            Catalog/  Organization/ (+IntegrationEvents/)  Judging/ (+Dtos/, IntegrationEvents/)
  Modules/
    BirraPoint.Catalog/            CatalogModule.cs  Domain/  Persistence/ (+Seeding/, Data/bjcp-2021.json)  Styles/  Integration/
    BirraPoint.Organization/       OrganizationModule.cs  Domain/ (incl. import batch/row entities)  Errors/  Persistence/
                                   Identity/  Competitions/  Import/  Judges/ (+Jobs/)  Integration/
    BirraPoint.Judging/            JudgingModule.cs  Domain/  Errors/  Persistence/  Tables/  TastingOrder/  Evaluations/
                                   Monitoring/  Integration/
    BirraPoint.Publication/        PublicationModule.cs  Domain/  Persistence/  Dispatch/ (+Jobs/)  Integration/
  BuildingBlocks/                  unchanged except Validation.MediatR overload
tests/BirraPoint.Api.UnitTests/    Architecture/ (rewritten), Modules/<M>/... mirrors new namespaces
tests/BirraPoint.Api.IntegrationTests/  unchanged layout; references Host + modules
```

`Errors/` stays at the module root (ADR-0038 location kept; the issue sketch's `Domain/OrganizationErrors.cs` is not
worth a second rename). Area `Setup.cs` replaces `Base<X>Endpoints.cs`; `<M>Module.cs` (`IModule`) replaces
`<M>Module.cs` + `<M>ModuleEndpoints.cs`.

**Structure Decision**: as above; graph and visibility in R-141-01/02.

### Slice map (old `Features/<Area>/Features/<Slice>` → new `<Area>/<Slice>`)

| Module | Area | Slices |
|---|---|---|
| Catalog | `Styles` (was `Catalog`) | GetStyles→`List`, GetStyleDetails→`Get` |
| Organization | `Competitions` | CreateCompetition→`Create`, GetCompetition→`Get`, ListCompetitions→`List`, UpdateCompetition→`Update`, ChangeCompetitionState→`ChangeState`, GetCompetitionCategories→`GetCategories`, SetCompetitionCategories→`SetCategories`, GetEntriesList→`ListEntries` |
| Organization | `Import` | UploadBeerImport→`UploadBeers`, GetBeerImport→`GetBeers`, EditBeerImportRow→`EditBeerRow`, ExcludeBeerImportRow→`ExcludeBeerRow`, RevalidateBeerImport→`RevalidateBeers`, ConsolidateBeerImport→`ConsolidateBeers`, UploadJudgeImport→`UploadJudges`, GetJudgeImport→`GetJudges`, EditJudgeImportRow→`EditJudgeRow`, ExcludeJudgeImportRow→`ExcludeJudgeRow`, ConsolidateJudgeImport→`ConsolidateJudges` |
| Organization | `Judges` | GetJudges→`List`, RegisterJudges→`Register`, NotifyJudges→`Notify`, ResendInvitation (kept), UpdateJudgeEmail→`UpdateEmail`; job handlers → `Judges/Jobs/` |
| Judging | `Tables` | CreateTable→`Create`, ListTables→`List`, UpdateTable→`Update` |
| Judging | `TastingOrder` | FixOrder (kept), GetMyTables (kept), GetTableJudges (kept), GetTableSamples (kept) |
| Judging | `Evaluations` | SubmitEvaluation→`Submit`, AdjustEvaluation→`Adjust`, CorrectEvaluation→`Correct`, CloseTable (kept), GetMyDiscrepancies (kept) |
| Judging | `Monitoring` | GetProgress (kept), GetEntryEvaluations (kept) |
| Publication | `Dispatch` | DispatchScoreSheet→`Dispatch`, GetDispatchStatus→`GetStatus`, GetResultsArchive→`GetArchive`, RefreshResults→`Refresh`, RetryDispatch→`Retry`; job handlers → `Dispatch/Jobs/` |

Endpoint `WithName(...)` operation ids keep their current values.

## Delivery (one PR per step, R-141-09)

| PR | Scope | Commits (in order) |
|---|---|---|
| 1 | spec docs; `BirraPoint.Common`, `BirraPoint.Contracts`, `IModule`/`ICommand`/`IQuery`; MediatR multi-assembly overload; architecture-test scaffolding (graph + layout rules parameterised per migrated module, empty set) | docs → tests (failing) → moves to Common/Contracts (`git mv` only) → code (visibility, usings, overload) |
| 2 | Catalog → `src/Modules/BirraPoint.Catalog` | tests flip Catalog to "migrated" (failing) → move → slice rename + `IModule` → read projections |
| 3 | Publication | same; job handlers to `Dispatch/Jobs/` |
| 4 | Judging | same + aggregate tests (failing) → aggregate commit (Evaluation, TastingTable, DiscrepancyAlert) |
| 5 | Organization | same + aggregate commit (Competition) |
| 6 | Host rename + Dockerfile/CI/labeler + AppHost; final architecture assertions (no `Modules/` left in Host); `Docs/modules.md`, ADR, living doc, CLAUDE.md, agents | move → code → docs |

The architecture tests take the set of migrated modules as data; each module PR adds its module to the set, which
makes the tests fail until the move lands (TDD for a refactor). After PR 5 the set equals all modules; PR 6 removes the
set and asserts globally.

## Test strategy

- **Regression net**: all existing unit, integration/contract and E2E suites run unchanged in assertions on every PR
  (SC-031). Only `using`s, project references and test setup (entity construction via constructors/factories) change.
- **New tests first**: architecture rules (R-141-06) with negative fixtures; aggregate unit tests per method mirroring
  the current rule tests (same cases, same errors) before the logic moves.
- **Migrations**: `dotnet ef migrations list` per context before/after (ids equal), `has-pending-model-changes` none,
  integration startup tests on an empty DB, manual start on an existing local DB (quickstart §3). `migration-safety` on
  PRs 1–5.
- **Wire**: existing contract tests + the blind-anonymity absence tests; plus a before/after diff of the Development
  OpenAPI paths (routes, methods, status codes) as an extra check, not a gate.
- **E2E**: `npm run e2e` on PR 4, 5 and 6 (judge and organizer flows); PR 2–3 rely on integration tests for their areas.
- **Gates per PR**: `dotnet build`, `dotnet format --verify-no-changes`, unit + integration tests; Docker build on PR 6.

## Owners

| Part | Agent |
|---|---|
| Projects, moves, slices, aggregates, `IModule`, MediatR overload, unit + architecture tests | backend-engineer |
| Integration test host/references, migration checks, E2E runs | qa-engineer |
| Dockerfile, `ci.yml`/`release.yml`/`labeler.yml` paths (PR 6) | infra-engineer |
| `Docs/modules.md`, ADR, `arquitectura_viva.md`, CLAUDE.md, agents' notes | docs-keeper |

## Risks

| Risk | Mitigation |
|---|---|
| Migration ids/namespaces | R-141-03; ids verified before/after; no `MigrationsAssembly` needed. |
| `WebApplicationFactory<Program>` after rename | `Program` stays public partial in Host; integration test project references Host (PR 6). |
| Internals needed by tests | `InternalsVisibleTo` per module project (R-141-02). |
| Silent loss of handlers/validators/configurations after split | MediatR multi-assembly overload; `GetType().Assembly` for EF configurations; startup/DI tests + full integration suite catch any missing route or handler. |
| Embedded seed resource name | `LogicalName` fixed (R-141-07); Catalog seed test. |
| OpenAPI schema id collisions | `CreateSchemaReferenceId` (R-141-04). |
| Long-lived branches drift | Sequential PRs off `main`, one module each. |
