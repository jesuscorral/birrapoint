# Tasks: One project per module with flat vertical slices

**Input**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [quickstart.md](./quickstart.md). Issue #141.
**Tests**: mandatory and first (constitution Principle III). Each test task is verified failing before its implementation task. The existing suites are the regression net: their assertions are never edited (SC-031).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- Task ids stay in this file, commits and the PR. They never go in code, comments or test names; cite FR-/SC-/US/ADR or the reason instead.
- Paths: `Api/` = `src/BirraPoint.Api/` (becomes `src/BirraPoint.Host/` in Phase 6), `Common/` = `src/BirraPoint.Common/`, `Contracts/` = `src/BirraPoint.Contracts/`, `M/<X>/` = `src/Modules/BirraPoint.<X>/`, `UT/` = `tests/BirraPoint.Api.UnitTests/`, `IT/` = `tests/BirraPoint.Api.IntegrationTests/`.
- **Commit rule (FR-111)**: inside each phase, pure moves (`git mv` + `node .claude/scripts/move-namespace.mjs`, no logic change) are one commit, before the commits that change code. Tasks marked **(move)** go in that commit.
- Each phase is one PR, opened after the previous one merges (R-141-09). Every phase ends with the gate task.

## Phase 1 (PR 1): Shared kernel, shared contracts, module shape — no module moved

**Goal**: `BirraPoint.Common` and `BirraPoint.Contracts` exist; modules (still in `Api/`) use them; `IModule`, `ICommand`/`IQuery` and the multi-assembly MediatR registration are available; architecture tests are ready to receive migrated modules.
**Independent test**: build + all suites green; architecture tests green with an empty "migrated modules" set; graph test fails on a fixture.

### Tests first

- [X] T001 [P] [US24] `UT/Architecture/ProjectGraphTests.cs`: reflection over `GetReferencedAssemblies()`: `BirraPoint.Common` and `BirraPoint.Contracts` reference no `BirraPoint.*` module or host assembly; Contracts references only Common + building blocks; for every assembly in `MigratedModules` (data source, empty for now) no reference to another module assembly or to the host; negative test with a fake assembly-reference set proves the rule reports the offender (FR-102, FR-103, R-141-06)
- [X] T002 [P] [US25] `UT/Architecture/SliceLayoutTests.cs`: file-system rules of R-141-06 §2 parameterised on `MigratedModules` (slice folder has `Endpoint.cs`+`Request.cs`+`Handler.cs`, optional `Response.cs`, nothing else; one `Setup.cs` per area; no `Features`/`CommonComponents`/`Base*Endpoints.cs`/`*ModuleEndpoints.cs`); negative tests on a temp-dir fixture (FR-104)
- [X] T003 [P] [US26] `UT/Architecture/CommandQueryTests.cs`: every MediatR request in a migrated module implements exactly one of `ICommand<>`/`IQuery<>`; query `Handler.cs` source rules of R-141-06 §4; aggregate rule of R-141-06 §5 (no public setter on state-protecting properties) parameterised on `ProtectedAggregates` (empty for now); negative tests on fixture sources/types (FR-105, FR-109)
- [X] T004 [P] [US24] `tests/BuildingBlocks/BuildingBlocks.Validation.MediatR.Tests`: `AddMediatRWithValidation(params Assembly[])` registers handlers and validators from every given assembly and the pipeline validates a request from the second assembly; existing single-assembly tests unchanged
- [X] T005 [US24] Run T001–T004; verify T001 (Common/Contracts missing) and T004 fail to compile/fail, T002/T003 pass on an empty set and their negative tests pass

### Implementation

- [X] T006 [US24] Create `Common/BirraPoint.Common.csproj` and `Contracts/BirraPoint.Contracts.csproj` (`BirraPointTargetFramework`, package refs only as needed: EF Core/Npgsql, MediatR, SignalR abstractions none); add to `BirraPoint.sln`; `Api/` references both; `InternalsVisibleTo` the two test projects
- [X] T007 [US24] **(move)** `git mv` into `Common/`: `Api/Domain/{Entity,DispatchJobType}.cs` (+`ITimestamped`) → `Common/Domain/`; `Api/Common/Persistence/{BirraPointDbContext,DbContextRegistration,DbSchemas,LikePattern,DispatchJobQueries,SharedTransaction,SharedTransactionInterceptor,UnitOfWorkSaveChangesInterceptor}.cs` and `Jobs/`, `Audit/` (contexts, design-time factories, migrations) → `Common/Persistence/`; `Api/Realtime/{CompetitionEvents,CompetitionGroups}.cs` and the `IEventPublisher` interface file → `Common/Realtime/`; `Api/Common/Keycloak/BirraPointKeycloak.cs` → `Common/Security/`; run `move-namespace.mjs`
- [X] T008 [US24] **(move)** `git mv` every `Api/Modules/<M>/Contracts/**` → `Contracts/<M>/**` (Catalog, Organization, Judging; `Dtos/` and `IntegrationEvents/` kept as subfolders); run `move-namespace.mjs`
- [X] T009 [US24] Make moved Common/Contracts types `public` (and their members used by modules); `BirraPointDbContext.ApplyConfigurationsOf` scans `GetType().Assembly` (R-141-03); policy-name and rate-limit-policy constants used by modules move to `Common/Security/Policies.cs` (host extensions consume them); `EventPublisher` stays in `Api/Realtime/` implementing the Common interface; fix remaining usings (`move-namespace.mjs --fix-build`)
- [X] T010 [P] [US24] `Common/Modules/IModule.cs`, `ICommand.cs`, `IQuery.cs` (R-141-04, R-141-07)
- [X] T011 [P] [US24] `src/BuildingBlocks/BuildingBlocks.Validation.MediatR/MediatRExtensions.cs`: `params Assembly[]` overload, single-assembly overload delegates; T004 green
- [X] T012 [US24] Update existing architecture tests (`ModuleBoundaryTests`, `ModuleLayoutTests`, `DbContextModelTests`, `ModuleBoundaryScanner*`) for the new namespaces of kernel and contracts (rules unchanged: integration events only under `Contracts/<Publisher>/IntegrationEvents`, no MediatR notification in contracts); T001 green
- [X] T013 [US27] `migration-safety` skill on the Jobs/Audit move; `dotnet ef migrations list` for `JobsDbContext`/`AuditDbContext` shows the same ids as `main`; `has-pending-model-changes` none
- [X] T014 Gate: `dotnet build BirraPoint.sln`, `dotnet format BirraPoint.sln --verify-no-changes`, unit + integration tests green; record ids from T013 in the PR

## Phase 2 (PR 2): Catalog module 🎯 first module

**Goal**: `M/Catalog/` is a project with flat slices; Host maps it through `IModule`.
**Independent test**: quickstart §1–§3 for Catalog.

- [ ] T015 [US24] Add `BirraPoint.Catalog` to `MigratedModules` in T001–T003; verify they fail (assembly missing)
- [ ] T016 [US24] Create `M/Catalog/BirraPoint.Catalog.csproj` (references Common, Contracts; `EmbeddedResource` `bjcp-2021.json` with `LogicalName`, R-141-07; `InternalsVisibleTo` test projects); add to sln; `Api/` references it
- [ ] T017 [US24] **(move)** `git mv Api/Modules/Catalog/* M/Catalog/` (Domain, Persistence incl. Seeding + Migrations, Integration, `Features/Catalog/Data/bjcp-2021.json` → `Persistence/Data/`); run `move-namespace.mjs`
- [ ] T018 [US25] Slices → `Styles/List`, `Styles/Get` with `Endpoint`/`Request`/`Handler`/`Response`; `Styles/Setup.cs` from `BaseCatalogEndpoints.cs`; `CatalogModule : IModule` (merge `CatalogModuleEndpoints.cs`); queries implement `IQuery<>` with `AsNoTracking` + `Select`; operation ids unchanged
- [ ] T019 [US24] `Api/Program.cs`: module list `IModule[]` (Catalog via `IModule`, the others still through their static extensions until their phase); `AddMediatRWithValidation(typeof(Program).Assembly, typeof(CatalogModule).Assembly)`; OpenAPI `CreateSchemaReferenceId` (R-141-04)
- [ ] T020 [US27] Update Catalog unit tests' usings/namespaces under `UT/Modules/Catalog/` (mirrors new namespaces; no assertion change); seed loader test proves resource resolves; T015 green
- [ ] T021 [US27] `migration-safety` on Catalog move; `migrations list` ids equal; gate as T014

## Phase 3 (PR 3): Publication module

- [ ] T022 [US24] Add `BirraPoint.Publication` to `MigratedModules`; verify failing
- [ ] T023 [US24] Create `M/Publication/BirraPoint.Publication.csproj` (Common, Contracts, BuildingBlocks.Jobs/EmailSender/Spreadsheet as used, QuestPDF); sln; `Api/` reference
- [ ] T024 [US24] **(move)** `git mv Api/Modules/Publication/* M/Publication/`; run `move-namespace.mjs`
- [ ] T025 [US25] Slices → `Dispatch/{Dispatch,GetStatus,GetArchive,Refresh,Retry}`; job handlers (`GeneratePdfsHandler`, `BundleZipHandler`, `SendResultEmail…`, `RefreshResultsHandler` if a job) → `Dispatch/Jobs/`; `CommonComponents/` → `Dispatch/Shared/`; `Dispatch/Setup.cs`; `PublicationModule : IModule`
- [ ] T026 [US26] Commands implement `ICommand<>`, queries `IQuery<>` with `AsNoTracking` + `Select`; retry write goes through a `Job`-side method only if one exists in the jobs block, otherwise unchanged (block untouched)
- [ ] T027 [US24] Host module list + MediatR assemblies include Publication; unit test usings; T022 green
- [ ] T028 [US27] `migration-safety`; ids equal; gate as T014

## Phase 4 (PR 4): Judging module

### Tests first

- [ ] T029 [US24] Add `BirraPoint.Judging` to `MigratedModules` and `Evaluation`, `TastingTable`, `DiscrepancyAlert` to `ProtectedAggregates`; verify failing
- [ ] T030 [P] [US26] `UT/Modules/Judging/Domain/EvaluationTests.cs`: `Submit`/`Adjust`/`Correct`/`Reopen`/`Lock` — every case of today's `SubmitEvaluationRules` and descriptor tests (caps 12/3/20/5/10, computed total ≤ 50, comments ≥ 20 chars/section, locked sheet rejected, adjust only while reopened) with the same `JudgingError`s
- [ ] T031 [P] [US26] `UT/Modules/Judging/Domain/TastingTableTests.cs`: `Close(facts)` (missing blind codes → `EvaluationsIncomplete` with `missing`, open discrepancy → `DiscrepancyOpen` with `blindCodes`, already closed → `TableClosed`, sets `ClosedAt`); `FixOrder` (cases of today's `TastingOrderRules`); judge/sample assignment cases of `TableAssignmentApplier`
- [ ] T032 [P] [US26] `UT/Modules/Judging/Domain/DiscrepancyAlertTests.cs`: open when totals > 7 apart, resolve/reconcile cases of `DiscrepancyRules`/`DiscrepancyReconciler`
- [ ] T033 [US26] Verify T030–T032 fail (methods missing)

### Implementation

- [ ] T034 [US24] Create `M/Judging/BirraPoint.Judging.csproj`; sln; `Api/` reference
- [ ] T035 [US24] **(move)** `git mv Api/Modules/Judging/* M/Judging/`; run `move-namespace.mjs`
- [ ] T036 [US25] Slices → `Tables/{Create,List,Update}`, `TastingOrder/{FixOrder,GetMyTables,GetTableJudges,GetTableSamples}`, `Evaluations/{Submit,Adjust,Correct,CloseTable,GetMyDiscrepancies}`, `Monitoring/{GetProgress,GetEntryEvaluations}`; `CommonComponents/` → `<Area>/Shared/` (DTOs, `OwnedCompetitionLookup`, `TableProjector`, `JudgeTableAccess`, descriptor catalog/serializer/validator); four `Setup.cs` keeping every route group, policy and tag; `JudgingModule : IModule`
- [ ] T037 [US26] Aggregate methods on `Evaluation`, `TastingTable`, `DiscrepancyAlert` (R-141-05), state-protecting setters `private`; handlers become load → method → save (locking reads stay in handlers); rule classes folded in and deleted; old rule unit tests replaced by T030–T032 (same cases); T030–T032 green
- [ ] T038 [US26] Commands `ICommand<>`, queries `IQuery<>` with `AsNoTracking` + `Select`; test setup that built entities with setters uses constructors/factories (`UT/`, `IT/` seeding helpers only)
- [ ] T039 [US24] Host module list + MediatR assemblies include Judging; T029 green
- [ ] T040 [US27] `migration-safety`; ids equal; `has-pending-model-changes` none (private setters, R-141-03); `npm run e2e` judge flows; gate as T014

## Phase 5 (PR 5): Organization module

- [ ] T041 [US24] Add `BirraPoint.Organization` to `MigratedModules` and `Competition` to `ProtectedAggregates`; verify failing
- [ ] T042 [P] [US26] `UT/Modules/Organization/Domain/CompetitionTests.cs`: `ChangeState` forward-only, no skip, every capability gate of `data-model.md` with today's `OrganizationError`s; `Update`/`SetCategories` Draft-only gates; verify failing
- [ ] T043 [US24] Create `M/Organization/BirraPoint.Organization.csproj` (Common, Contracts, BuildingBlocks.Identity/Keycloak.Admin/Spreadsheet/EmailSender/Jobs as used); sln; `Api/` reference
- [ ] T044 [US24] **(move)** `git mv Api/Modules/Organization/* M/Organization/`; import batch/row entities (`Features/Import/*Batch.cs`, `*Row.cs`) → `Domain/`; run `move-namespace.mjs`
- [ ] T045 [US25] Slices per plan §Slice map for `Competitions`, `Import`, `Judges`; job handlers → `Judges/Jobs/`; `CommonComponents/` → `<Area>/Shared/`; three `Setup.cs` (NotifyJudges rate-limit policy kept); `OrganizationModule : IModule`
- [ ] T046 [US26] `Competition.ChangeState/Update/SetCategories` (R-141-05), private setters; handlers load → method → save; T042 green
- [ ] T047 [US26] Commands/queries markers, read projections; test seeding via constructors/factories
- [ ] T048 [US24] Host: all four modules through `IModule`; static `Add<M>Module`/`Map<M>Module` extensions gone; `Api/Modules/` empty and removed; T041 green
- [ ] T049 [US27] `migration-safety`; ids equal; `npm run e2e` organizer flows; gate as T014

## Phase 6 (PR 6): Host rename, container, docs — closes #141

- [ ] T050 [US24] Architecture tests: drop the `MigratedModules` set, assert globally (all module assemblies; host contains no `Modules` namespace; quickstart §2 grep is a test); verify it fails before T051 only if anything remains
- [ ] T051 [US24] **(move)** `git mv src/BirraPoint.Api src/BirraPoint.Host`, rename `.csproj`; run `move-namespace.mjs` (namespace `BirraPoint.Host.*`)
- [ ] T052 [US24] `BirraPoint.sln`, `src/Aspire/BirraPoint.AppHost` (`Projects.BirraPoint_Host`, resource name `api` unchanged), test project references, `InternalsVisibleTo`; `WebApplicationFactory<Program>` compiles against Host (R-141-08)
- [ ] T053 [P] infra: `src/BirraPoint.Host/Dockerfile` (restore all project files, `ENTRYPOINT BirraPoint.Host.dll`, image name unchanged); `.github/workflows/ci.yml` + `release.yml` dockerfile path; `.github/labeler.yml` globs (`src/BirraPoint.Host/**`, `src/BirraPoint.Common/**`, `src/BirraPoint.Contracts/**`, `src/Modules/**`, migrations globs for every `Migrations/` folder); local `docker build -f src/BirraPoint.Host/Dockerfile .`
- [ ] T054 [P] docs: `Docs/modules.md` (FR-112 per module: routes + policy, tables/schema, contracts exposed/consumed, integration events published/handled, jobs); ADR `Docs/adrs/0044-one-project-per-module.md` superseding ADR-0031 and layout parts of ADR-0033/0034; `Docs/arquitectura_viva.md` links `modules.md`; CLAUDE.md Layout + Backend conventions + commands (`dotnet ef ... --project src/Modules/BirraPoint.<M> --startup-project src/BirraPoint.Host`); `.claude/agents/*.md` scope paths; `.claude/skills/migration-safety` paths; `move-namespace.mjs` header comment
- [ ] T055 [US27] Full quickstart §1–§4 (existing local DB 0 migrations; empty DB six histories; `npm run e2e`); startup time on an up-to-date DB compared with `main` (same machine, 3 runs) and `k6 run infra/perf/api-budgets.js` when a bearer token is available, else record why (SC-034); gate as T014 + Docker build

## Dependencies

- Phases are strictly sequential (one PR each, merged before the next starts).
- Inside a phase: tests-first tasks → move commit → code tasks → gate. `[P]` tasks may run in parallel.
- T053 and T054 run in parallel once T052 builds.
