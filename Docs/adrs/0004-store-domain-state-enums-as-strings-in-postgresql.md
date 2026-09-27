# 0004 - Store domain state enums as strings in PostgreSQL

**Status:** Accepted
**Date:** 2026-07-08

## Context

EF Core stores enums as `int` by default. The state/status enums (`CompetitionState`,
`TableState`, `EvaluationStatus`, `InvitationStatus`, `DiscrepancyStatus`, `DispatchJobType`,
`DispatchJobStatus`) already travel as strings in the REST contract, and ints would make direct SQL,
audit diffs and the partial index on open discrepancy alerts opaque.

## Decision

Map every domain enum with `HasConversion<string>()` and an explicit `HasMaxLength`. The partial
unique index on `DiscrepancyAlert` filters on `WHERE "Status" = 'Open'`.

## Consequences

- Rows read like the contract (`'InEvaluation'`, not `2`); reordering enum members cannot
  renumber existing data. Renaming a member needs a migration.
- Slightly wider columns and string comparisons — irrelevant at this scale.
- Every new domain enum must follow this convention; reviewers flag int-backed ones.
