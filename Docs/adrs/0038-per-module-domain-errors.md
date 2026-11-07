# 0038 - Per-module domain errors

**Status:** Accepted
**Date:** 2026-11-07

## Context
ADR-0033/0034 split the API into modules that only talk through `Contracts/`, so a module can become its own service later. Domain errors still lived in the host: one `DomainErrorType` enum, one `DomainErrorCatalog` and one `DomainException` in `Common/Errors/`, used by Organization and Judging. A module moved to another service could not reach `Common`, so the catalog would have to be copied or the module rewritten. ADR-0037 kept the catalog in the host only because it is API contract, not because it is shared code.

## Decision
- Each module that throws domain errors owns `Modules/<M>/Errors/{<M>Error,<M>ErrorCatalog,<M>Exception}.cs`.
  - Organization (5): InvalidImportFile, UnresolvedImportRows, TablesStillOpen, JudgeAlreadyActive, InvalidStateTransition.
  - Judging (9): ConflictOfInterest, OrderAlreadyFixed, OrderNotFixed, OutOfSequence, EvaluationLocked, TableClosed, EvaluationsIncomplete, DiscrepancyOpen, InvalidStateTransition.
  - Catalog and Publication throw none.
- Each `Add<M>Module()` registers its own mapping with `AddProblemDetailsMapping(map => map.Map<<M>Exception>(...))`.
- The host keeps only cross-cutting errors in `Common/Errors/{HostError,HostErrorCatalog}.cs` (Validation, RateLimited) and the validation mapping in `ErrorHandlingExtensions`. `DomainErrorType`, `DomainErrorCatalog` and `DomainException` are deleted.
- `InvalidStateTransition` exists in both Organization and Judging with the same URN, `409` and title. The URN is the wire contract, not shared code.
- Guards: `ModuleBoundaryTests.ModulesDoNotReferenceTheHostErrorsNamespace`; `ErrorCatalogTests` checks that the union of host and module catalogs equals the 15 rows of `contracts/rest-api.md` §Error catalog and that shared URNs have identical status and title.

## Alternatives
| Option | Verdict |
|---|---|
| One catalog in `Common/Errors` (status quo) | Rejected: unreachable once a module is a separate service. |
| Move the catalog to the Catalog module | Rejected: Catalog is BJCP styles and throws none; Organization and Judging would depend on it. |
| Shared NuGet error package | Rejected for now: a package for URN strings is premature; revisit if a third module needs the same error. |

## Consequences
- Modules are closer to extractable: no error type crosses a module boundary.
- A shared URN (`invalid-state-transition`) is duplicated; `ErrorCatalogTests` fails if status or title drift.
- No response change. The contract stays the single closed list (constitution Principle VI); adding an error still starts in `contracts/rest-api.md`.
- Supersedes the "stays in the host" note for the catalog and `DomainException` in ADR-0037.
