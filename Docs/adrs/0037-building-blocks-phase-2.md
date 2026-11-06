# 0037 - BuildingBlocks phase 2: Dapr secrets, Keycloak, ProblemDetails, validation, spreadsheets and jobs

**Status:** Accepted
**Date:** 2026-11-06

## Context
ADR-0035 extracted the first two cross-cutting blocks (Audit, EmailSender) under the rules it set: no BirraPoint reference (asserted by a test), public API in `BuildingBlocks.<Capability>`, only the packages the block needs, `net10.0` set literally, host-agnostic tests. An analysis of `Common/`, `Realtime/`, `Domain/` and the modules found more capabilities that are not BirraPoint business logic and are coupled to it only by configuration keys, a role name, an enum or `AppDbContext`. Not extracted, on purpose: `Realtime/` (hub and audiences are domain), `RateLimiting/` (one domain policy), the score-sheet PDF and ZIP (domain or trivial), the persistence helpers (`ITimestamped` stamping, `LikePattern`, `StartupMigrations`, `DatabaseHealthCheck`: useful but not cohesive; one block for all of them is the "single BuildingBlocks project" ADR-0035 rejected), and `DomainErrorCatalog` (API contract).

## Decision
Five more capabilities become blocks, delivered in this order (simplest and least coupled first), each in its own task and PR, with no behaviour or contract change:

| Order | Block(s) | Packages | Generic part | Stays in BirraPoint |
|---|---|---|---|---|
| 1 | `BuildingBlocks.DaprSecrets` | `Dapr.Extensions.Configuration` | Load named secrets from a Dapr secret store into `IConfiguration` at startup: sidecar wait, retries on store errors (403 while RBAC propagates), `--` → `:` key mapping, opt-in through a configuration key | The secret list (`ConnectionStrings--db`, `ConnectionStrings--dbDirect`, `Keycloak--AdminClientSecret`, optional `Smtp--Password`) and the `Dapr:SecretStore` key |
| 2 | `BuildingBlocks.Keycloak.Authentication` | `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT bearer against a Keycloak realm (authority, audience, raw claim names), realm roles → role claims (`realm_access.roles`), optional `access_token` query for given hub paths | Fallback policy, ORGANIZER/JUDGE policies, `/hubs/competition`, `ICurrentUser` and the resolvers |
| 2 | `BuildingBlocks.Keycloak.Admin` | none beyond the framework (`HttpClient`) | Client-credentials admin client: ensure a user by email with a temporary password and `UPDATE_PASSWORD` (existing set-up accounts keep theirs), grant a realm role, change a user's email; options class with fail-fast validation of the client secret (placeholder rejected outside Development/Testing) | Which role (`JUDGE`), the `Keycloak` section values, the placeholder text |
| 3 | `BuildingBlocks.ProblemDetails` | framework only | RFC 7807 writers: a mapping hook from any exception type to (type URN, status, title, detail, extensions) and a catch-all 500 that never leaks message or stack; no FluentValidation or MediatR | `urn:birrapoint:*` catalog, `DomainException`, the 429 writer |
| 3 | `BuildingBlocks.Validation.MediatR` | `MediatR` 12.x (pinned, licensing), `FluentValidation` | Pipeline behavior running every validator and throwing one `ValidationException`; its 400 mapping (field errors as `errors`) registered through `BuildingBlocks.ProblemDetails`, the only block-to-block reference | The validation type URN (`urn:birrapoint:validation`), passed as an option |
| 4 | `BuildingBlocks.Spreadsheet` | `ClosedXML` | Read the first worksheet of an `.xlsx` by header name (case-insensitive, trimmed), report missing required headers, typed cell readers (text, numeric-or-text, date, date-time, number: value / present / error); a neutral format exception | Column names, row parsing, catalog matching, the mapping of the format exception to `invalid-import-file` |
| 5 | `BuildingBlocks.Jobs` | `Microsoft.EntityFrameworkCore.Relational`, `Npgsql.EntityFrameworkCore.PostgreSQL` | Database-backed job queue: `Stage` (commits with the caller's unit of work) and `Enqueue`, worker with atomic claim (`FOR UPDATE SKIP LOCKED`), lease renewal and expired-lease recovery, retry with capped exponential backoff, wake-up signal plus safety-net poll, idempotent handlers keyed by a string job type, an observer hook for state changes | `DispatchJobType` names, the handlers, the `DispatchProgress` SignalR event, the foreign key to `Competition` |

Rules added to ADR-0035's:

- Both Keycloak blocks validate their options at startup (`ValidateOnStart`): Authentication fails when `Authority` or audience is blank in any environment; Admin also requires `Authority` and `AdminClientId` outside Development/Testing. Messages are generic (no BirraPoint names).
- Keycloak is two projects, so a service that only validates tokens doesn't carry the admin client and the reverse.
- ProblemDetails has no MediatR dependency; `BuildingBlocks.Validation.MediatR` is the only block that takes MediatR, pinned like the API.
- `BuildingBlocks.Jobs` is PostgreSQL-only (documented in its public API). It keeps BirraPoint's existing table: the host maps table and column names through `ApplyJobs(configure)`, the job type and status are already stored as strings, and the group key (`CompetitionId` today) is a host-named column; the block reads table and column names from the EF model for its raw SQL instead of hard-coding them. No data migration; the task proves it with an empty `dotnet ef migrations add` diff.
- Existing tests that cover a moved capability move with it to `tests/BuildingBlocks/BuildingBlocks.<X>.Tests` and run against host-agnostic fixtures; BirraPoint keeps only the tests of what stays (its catalog, its secret list, its role, its handlers).

## Consequences
- Seven more projects in the solution; each is independently testable and publishable as a NuGet package later (ADR-0035).
- `contracts/` are unchanged: URNs, the `DispatchProgress` payload and the dispatch status API keep their shape; contract tests are the check.
- Jobs is the riskiest move (17 call sites, 12 test files, concurrency tests): it goes last, after the simpler blocks have settled the pattern, and its PR runs the `migration-safety` skill.
- A host that wants Jobs on another database would need a new claim query; accepted, since every planned consumer uses PostgreSQL.
