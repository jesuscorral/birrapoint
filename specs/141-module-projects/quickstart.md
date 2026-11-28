# Quickstart validation: One project per module (#141)

One scenario per story. Run at the end of every module PR (for the modules migrated so far) and fully on PR 6.

## 1. Boundaries are compiler-enforced (US24, SC-029)

1. In `src/Modules/BirraPoint.Judging`, add `<ProjectReference>` to `BirraPoint.Organization` and use one of its internal
   types → `dotnet build BirraPoint.sln` fails (`CS0122` or the architecture graph test fails). Revert.
2. Without the reference, use an Organization type by name → `CS0246`. Revert.
3. `dotnet test tests/BirraPoint.Api.UnitTests --filter "FullyQualifiedName~Architecture"` → green.

## 2. Slices are findable (US25, SC-030, SC-033)

1. Pick `POST /api/v1/me/tables/{tableId}/close` from `rest-api.md` → `src/Modules/BirraPoint.Judging/Evaluations/CloseTable/`
   holds `Endpoint.cs`, `Request.cs`, `Handler.cs`; `Evaluations/Setup.cs` maps it.
2. `git ls-files src | grep -E "Features/.*/Features/|CommonComponents/|Base[A-Za-z]+Endpoints\.cs|ModuleEndpoints\.cs"` → empty.

## 3. Nothing changes outside (US27, SC-031, SC-032)

1. `dotnet test tests/BirraPoint.Api.UnitTests` and `dotnet test tests/BirraPoint.Api.IntegrationTests` → green;
   `git diff main --stat -- tests` shows no assertion edits (only usings/setup).
2. For each context: `dotnet ef migrations list --context <X>DbContext --project <project> --startup-project src/BirraPoint.Host`
   → same ids as on `main`; `dotnet ef migrations has-pending-model-changes` → none.
3. Existing local DB: start `dotnet run --project src/Aspire/BirraPoint.AppHost` on a DB migrated by `main` → API logs show
   0 migrations applied; organizer and judge happy paths work.
4. Empty DB: Aspire dashboard → `db` → Reset database → start → all six histories created with the same ids.
5. `cd src/Birrapoint.Web && npm run e2e` → green.

## 4. Rules live on aggregates (US26)

1. `dotnet test tests/BirraPoint.Api.UnitTests --filter "FullyQualifiedName~Domain"` → aggregate tests green (state
   machine, caps/total/comments, lock/reopen, table close, fixed order).
2. Temporarily add `.AsTracking()` or `SaveChangesAsync` to a query handler → architecture test fails. Revert.
