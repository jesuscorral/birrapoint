# Research: One project per module with flat vertical slices (#141)

Decisions taken while planning. Each one says what was chosen, why, and what was rejected.

## R-141-01 Project graph

**Decision**:

```text
BirraPoint.Host ──► BirraPoint.Catalog ─┐
               ├──► BirraPoint.Organization ─┤
               ├──► BirraPoint.Judging ──────┼──► BirraPoint.Contracts ──► BirraPoint.Common ──► BuildingBlocks.*
               └──► BirraPoint.Publication ──┘          │                         ▲
                                                        └─────────────────────────┘
```

- `src/BirraPoint.Common` (shared kernel, public API): `Entity`, `ITimestamped`, `DispatchJobType`, `IModule`,
  `ICommand<T>`/`IQuery<T>` markers, `BirraPointDbContext`, `DbContextRegistration` (+ `MigrationTarget`),
  `SharedTransaction` + interceptors, `DbSchemas`, `LikePattern`, `DispatchJobQueries`, **`JobsDbContext` and
  `AuditDbContext` with their migrations**, `IEventPublisher` + `CompetitionGroups` + `CompetitionEvents`,
  policy names (`ORGANIZER`/`JUDGE`, NotifyJudges rate-limit policy), `BirraPointKeycloak` constants.
- `src/BirraPoint.Contracts`: `Catalog/`, `Organization/`, `Judging/` (interfaces, snapshots, DTOs exposed to other
  modules, `IntegrationEvents/`). References `BirraPoint.Common` and `BuildingBlocks.Messaging` only.
- `src/Modules/BirraPoint.<M>`: references `Common`, `Contracts` and the building blocks it uses. Never another module.
- `src/BirraPoint.Host` (renamed from `BirraPoint.Api`, `Microsoft.NET.Sdk.Web`): `Program.cs`, module list,
  `AuthenticationExtensions`, `RateLimitingExtensions`, `ErrorHandlingExtensions` + `HostError*`, `BirraPointSecrets`,
  `CompetitionHub`, `EventPublisher` (implements `IEventPublisher` over `IHubContext<CompetitionHub>`),
  `DispatchProgressObserver`, `CurrentUserAuditActor`, `StartupMigrations`, `LegacyDatabaseGuard`,
  `DatabaseHealthCheck`, the design-time factory.

**Why**: compiler-enforced boundary (FR-102/103). `JobsDbContext` sits in Common, not Host as the issue sketch
says: Organization (`NotifyJudges`, invitation handlers) and Publication (dispatch, retry, refresh, archive) read and
stage jobs in the same unit of work (FR-096); putting the context in Host would force a module→Host reference (a
cycle). `AuditDbContext` follows it for symmetry; the audit writer stays registered by the Host.
**Rejected**: Jobs/Audit in Host (cycle); a `BirraPoint.Persistence` project between Common and modules (one more
project, no boundary gained).

## R-141-02 Visibility

**Decision**: Common and Contracts types are `public`. Module types stay `internal`, except the module class that
implements `IModule` (`public sealed class JudgingModule : IModule`). Each module grants
`InternalsVisibleTo` to `BirraPoint.Api.UnitTests` and `BirraPoint.Api.IntegrationTests` (and `DynamicProxyGenAssembly2`
only if a test mocks an internal interface today). Host does not get internals of any module.
**Why**: FR-110; `internal` is what makes the compiler reject a cross-module use of an internal type even when a
reference slipped in. **Rejected**: everything public (boundary only by convention again).

## R-141-03 Migrations keep their ids

**Decision**: `git mv` migration, designer and snapshot files with their context; only the `namespace` line changes
(`move-namespace.mjs`). `[Migration("20261127124648_InitialCreate")]` and `[DbContext(typeof(...))]` stay. No
`MigrationsAssembly(...)` call is needed: EF defaults to the context's assembly, and each context moves together with
its migrations. `BirraPointDbContext.ApplyConfigurationsOf` changes from `typeof(BirraPointDbContext).Assembly` to
`GetType().Assembly` (the configurations live in the module assembly now). Each module keeps its
`<M>DbContextDesignTimeFactory`, so `dotnet ef ... --project src/Modules/BirraPoint.<M> --startup-project src/BirraPoint.Host`
works. Verified by: `dotnet ef migrations list` per context shows the same ids; `dotnet ef migrations has-pending-model-changes`
reports none; integration startup tests on an empty DB; manual start on an existing local DB applies 0 migrations
(quickstart §3). `migration-safety` skill runs on each module PR. **Rejected**: regenerating baselines (breaks
FR-108 and every existing local DB).

## R-141-04 Slice shape and names

**Decision**:

```text
<Module>/<Area>/Setup.cs                  internal static class Setup { MapEndpoints(IEndpointRouteBuilder) }  route groups + policies
<Module>/<Area>/<Slice>/Endpoint.cs       internal static class Endpoint { Map(RouteGroupBuilder) }  route, binding, Results.*, WithName (operation id unchanged)
<Module>/<Area>/<Slice>/Request.cs        internal sealed record Request(...) : ICommand<Response> | IQuery<Response>
                                          + internal sealed class Validator : AbstractValidator<Request>   (when it validates)
<Module>/<Area>/<Slice>/Handler.cs        internal sealed class Handler(...) : IRequestHandler<Request, Response>
<Module>/<Area>/<Slice>/Response.cs       internal sealed record Response(...)  (only when the slice returns a body)
<Module>/<Area>/Shared/                   helpers, DTOs shared by slices of the area
<Module>/<Area>/Jobs/                     IJobHandler implementations of the area (not slices)
```

Namespace = folder, e.g. `BirraPoint.Judging.Tables.Create`. Slice folders are short verbs when the object is the area
(`Tables/Create`, `Tables/List`, `Evaluations/Submit`), verb + object otherwise (`Evaluations/CloseTable`,
`Judges/ResendInvitation`). The slice rename map is in `plan.md` §Slice map. `ICommand<T>`/`IQuery<T>` are empty
markers deriving from MediatR `IRequest<T>`; no pipeline change (FR-107).
**OpenAPI**: dozens of types named `Response`/`Request` would collide in schema ids; the host sets
`options.CreateSchemaReferenceId` to a name built from the namespace (`JudgingTablesCreateResponse`). The OpenAPI document
is Development-only and not a contract (contracts live in `rest-api.md`), but stays readable.
**Rejected**: keeping slice-prefixed type names (the issue's explicit complaint); one file per slice (handler +
validator + request mixed again).

## R-141-05 DDD on writes, projections on reads

**Decision**: aggregates own their invariants as methods that throw the module exception (ADR-0038 unchanged):

| Aggregate | Methods (moved from) |
|---|---|
| `Competition` (Organization) | `ChangeState(target)` – forward-only, no skip, capability gates (`ChangeCompetitionState` handler); `Update(...)` with the Draft-only gates (`UpdateCompetition`); `SetCategories(...)` (`SetCompetitionCategories`) |
| `Evaluation` (Judging) | `Submit(scores, comments, descriptors)` with caps 12/3/20/5/10, total, ≥ 20 chars (`SubmitEvaluationRules`); `Adjust(...)` only while reopened by an open discrepancy; `Correct(...)` (organizer, audit stays in handler); `Reopen()`/`Lock()` |
| `TastingTable` (Judging) | `Close(facts)` – complete sheets + no open discrepancy (`CloseTableRules` + inline checks); `FixOrder(...)` (`FixOrder` handler, `TastingOrderRules`); assign/remove judges and samples (`TableAssignmentApplier`) |
| `DiscrepancyAlert` (Judging) | `Open`/`Resolve` (`DiscrepancyRules`, `DiscrepancyReconciler`) |

Cross-aggregate facts (judges active at the table, submitted pairs, blind codes from Organization contracts) are loaded by
the handler and passed in; aggregates never call contracts or the context. State-protecting properties get
`private set` (EF maps private setters); construction goes through constructors/factory methods. Pure rule classes
fold into the aggregate or a `Domain/` service; their unit tests are rewritten against the aggregate method **with
the same cases and expected errors** (only the call site changes, SC-031). Read handlers: `AsNoTracking()` +
`Select(...)` to the response; locking reads (`FOR UPDATE`) only in write handlers.
**Rejected**: rich domain everywhere (Participant, Organizer, Invitation stay simple – no invariant to protect);
domain events inside the module (MediatR notifications are banned by an existing architecture test).

## R-141-06 Architecture tests (FR-109)

**Decision**: rewrite `tests/BirraPoint.Api.UnitTests/Architecture/` against the new assemblies:

1. **Project graph** – reflection over `GetReferencedAssemblies()`: no module assembly references another module;
   Common and Contracts reference no module (replaces `ModuleBoundaryScanner`'s namespace scan; the compiler now enforces
   most of it, the test documents and guards the `.csproj` graph).
2. **Layout** – file-system scan of `src/Modules/*/`: every folder that contains `Endpoint.cs` also contains `Request.cs`
   and `Handler.cs`; no other file names in a slice folder except `Response.cs`; one `Setup.cs` per area; no
   `Features`, `CommonComponents`, `Base*Endpoints.cs`, `*ModuleEndpoints.cs` anywhere.
3. **Commands vs queries** – reflection: every MediatR request implements exactly one of `ICommand<>`/`IQuery<>`.
4. **Reads do not track** – source scan of each `Handler.cs` whose `Request` is an `IQuery<>`: if it touches a
   `DbSet`/context it contains `AsNoTracking`, and it never contains `SaveChanges`, `FOR UPDATE`, `.Find(`/`FindAsync`,
   `Attach`, `Update(`.
5. **Writes go through aggregates** – reflection on the aggregates of R-141-05: no public setter on state-protecting
   properties; the listed methods exist.
6. Kept: DbContext model tests (each context maps its schema only, FR-099), no MediatR notifications in contracts,
   integration events only under `BirraPoint.Contracts/<Publisher>/IntegrationEvents`.

Each new rule gets a negative test on a fixture (as `ModuleBoundaryScannerTests` does today) so it is shown to fail.

## R-141-07 Registration and scanning

**Decision**: `IModule` in Common:

```csharp
public interface IModule
{
    void AddServices(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
```

Host: `IModule[] modules = [new CatalogModule(), new OrganizationModule(), new JudgingModule(), new PublicationModule()];`
one `foreach` for services, one for endpoints, same order as today (route registration order and migration order kept,
FR-097). `BuildingBlocks.Validation.MediatR.AddMediatRWithValidation` gets a `params Assembly[]` overload (the single-
assembly one delegates to it; covered by its existing test project); Host passes the four module assemblies. EF
configuration scanning per R-141-03. Job handlers and messaging stay explicitly registered by each module (no scan).
Catalog's `bjcp-2021.json` moves as `EmbeddedResource` to the Catalog project with `LogicalName="bjcp-2021.json"`
so the loader and the baseline seed migration keep resolving it regardless of root namespace.
**Rejected**: Scrutor/assembly scanning of `IModule` (hides the list the issue wants explicit; no new deps).

## R-141-08 Host rename and container

**Decision**: `git mv src/BirraPoint.Api src/BirraPoint.Host`; project, assembly and root namespace `BirraPoint.Host`.
Integration tests keep `WebApplicationFactory<Program>`; `Program` is the Host's (public partial). AppHost references
`BirraPoint.Host.csproj`; the Aspire resource name stays `api` (ports, env and Keycloak redirect config unchanged).
Dockerfile moves to `src/BirraPoint.Host/Dockerfile`, restores via the `.csproj` files of Host, Common, Contracts and
`src/Modules/*`, `ENTRYPOINT ["dotnet", "BirraPoint.Host.dll"]`; image name and tags unchanged, so Terraform is
untouched. `.github/workflows/ci.yml` + `release.yml` dockerfile paths and `.github/labeler.yml` globs follow.
**Rejected**: keeping `BirraPoint.Api` (user chose the rename).

## R-141-09 Delivery as six PRs

**Decision** (clarified): PR 1 = spec/plan/tasks + Common + Contracts + `IModule` + MediatR overload + new architecture
test scaffolding, modules still inside `BirraPoint.Api` (they move their kernel `using`s to `BirraPoint.Common`, and
their contract types to `BirraPoint.Contracts`). PR 2 Catalog, PR 3 Publication, PR 4 Judging, PR 5 Organization
(each: move commit, then rename-slices commit, then aggregate commit when it applies). PR 6 = rename Api→Host, Docker/CI
paths, `Docs/modules.md`, ADR, CLAUDE.md, agents, `move-namespace.mjs`. Until PR 6 the host project is still
`BirraPoint.Api`; module projects are referenced from it. Branches: `feature/141-module-projects` (PR 1), then
`feature/141-<step>` off `main` after the previous merge.
**Why**: each PR touches one module and leaves `main` green (FR-111).

## R-141-10 `move-namespace.mjs`

**Decision**: no logic change. The script already derives each file's namespace from the nearest `.csproj`
(project name + relative folder), so new projects work as they are created. Each move commit runs it with `--dry-run`
first; only its header comment and CLAUDE.md usage notes are updated (PR 6) to name the new project roots.
