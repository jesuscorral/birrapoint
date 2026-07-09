# 0004 - Store domain state enums as strings in PostgreSQL

**Status:** Accepted
**Date:** 2026-07-08

## Context

T009 introduces `AppDbContext` and the initial EF Core migration for the seven state/status
enums in the domain model (`CompetitionState`, `TableState`, `EvaluationStatus`,
`InvitationStatus`, `DiscrepancyStatus`, `DispatchJobType`, `DispatchJobStatus`). EF Core's
default mapping stores enums as their underlying `int`, which is compact but opaque: reading
`AuditLog.DataJson` diffs, querying the database directly during incident response, or writing
the partial unique index filter for open `DiscrepancyAlert` rows (data-model.md) all require the
value to be human-readable. The API contract (`contracts/rest-api.md`) already serializes these
states as strings (e.g. `"Draft"`, `"PendingConsensus"`), so an int mapping would require a
translation layer purely for the database that does not exist anywhere else in the system.

## Decision

Map every domain enum to a `string` column via EF Core's `HasConversion<string>()`, with an
explicit `HasMaxLength` per column (`Common/Persistence/Configurations/*.cs`). The partial
unique index on `DiscrepancyAlert` filters on the literal SQL string
(`WHERE "Status" = 'Open'`), which only reads naturally with a string-backed column.

## Consequences

- SQL run directly against the database (support, migrations review, ad-hoc audits) shows
  `'InEvaluation'`, not `2`, matching the wire contract and the state-machine diagrams in
  `data-model.md` — no lookup table needed to make sense of a row.
- Reordering or inserting enum members in C# cannot silently renumber existing rows (a known
  footgun of int-backed enums); a rename still requires a migration, same as any column rename.
- Slightly larger column width than an int, and equality/index comparisons are string
  comparisons rather than integer — immaterial at this dataset's scale (competition-scoped
  rows, not a high-cardinality hot path).
- Every future enum added to the domain must follow the same convention for consistency; a
  reviewer should flag an int-backed state/status enum in future PRs against this ADR.
