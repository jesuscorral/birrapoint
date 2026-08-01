# 0011 - Fold entry import into wizard step 4, replace with the ACCE spreadsheet format

**Status:** Accepted
**Date:** 2026-07-31

## Context

`Features/Import/` (US3, FR-009–FR-012) shipped against a generic, English-header `.xlsx`
contract (`ParticipantName`/`ParticipantEmail`/`BeerName`/`Style`/`Collaborators`) invented for
the MVP spec. It was never linked anywhere in the UI (`organizer/competitions/:id/import` had no
`routerLink` pointing at it) and its correction screen only let the organizer fix the `Style`
cell — every other field was immutable after parsing.

The organizer's real club (ACCE) spreadsheet is a different, richer shape entirely: Spanish
headers, brewer contact/membership fields (email, ACCE member number, birth date, phone), a
`Categoria` column that must resolve against the organizer's own step-3 `CompetitionCategory`
names (not just the BJCP catalog), an `Estilo` column formatted as `"<code>. <name>"`, no
beer-name column at all, and a full recipe (ABV, brew/bottling dates, malts/hops/yeast/other
ingredients, judge-facing entry instructions). The organizer also needs to review and correct
every field, not just style, before consolidating.

## Decision

1. **Replace, don't dual-support, the file format.** `WorkbookParser` now implements only the
   ACCE column set (`contracts/import-file.md`, rewritten). Nothing depended on the generic
   format in production, so carrying both would only add branching with no user benefit.
2. **`BeerEntry.BeerName` becomes optional** (`string?`). The source file has no beer-name
   column; blind code + style + category still uniquely identify a sample. The organizer may
   still type one in during review.
3. **`Participant` stays scoped per competition** (`(CompetitionId, Email)`, unchanged) rather
   than becoming a global cross-competition identity. The same brewer can legitimately register
   under a different name in a different competition; what changed is that a *repeat* email
   *within one competition's import* now updates the existing `Participant` row (last-import-wins)
   instead of only creating-if-absent.
4. **New `CategoryMismatch` row status**, mirroring `StyleMismatch` exactly: the `Categoria` cell
   must exact-match (case-insensitive) one of the competition's own `CompetitionCategory` names;
   a miss blocks consolidation until the organizer picks an existing category or excludes the row.
5. **The narrow `assign-style`/`exclude` resolve action is replaced by a full row-edit `PUT`**
   accepting every editable field at once, since the organizer now needs to correct any cell, not
   just style. `exclude` stays a separate, tiny, one-way endpoint.
6. **`EntryInstructions` is a deliberate, singular exception to the blind-anonymity boundary**
   (BR-01/FR-019): it's added to judge-facing DTOs (`JudgeSampleDto`, `TableSampleDto`) because the
   organizer/brewer may need to tell the judge something operationally relevant ("serve in one
   pour") without identifying the brewer or beer name. `BeerName` and every `Participant.*` field
   remain permanently excluded from anything a judge reads — this ADR does not relax that
   invariant, it documents the one new, intentional field that does cross it.
7. **The import screen moves into the wizard as step 4**, rebuilt to match the wizard's visual
   language (it was previously unstyled). `CategoriesStepComponent` (step 3) is no longer
   terminal — its finish action now emits `saved` and lets the wizard advance, the same pattern
   already used when step 3 itself was added ahead of step 2.

## Consequences

- One new EF Core migration widens `Participant`/`BeerEntry`/`ImportRow` and adds
  `CompetitionCategoryId` (FK, required) to `BeerEntry`. Purely additive/widening — no
  backfill risk, since Phase 16 (deployment) hasn't started and no real data exists yet.
- Any future format flavor (a second club's template, say) would need either a header-based
  format auto-detector or a parallel parser — deliberately out of scope here; nothing today
  asks for it.
- The row-level edit contract is now "send the whole row back" (full replace) rather than a
  small action payload, consistent with this codebase's existing PUT conventions
  (`UpdateCompetition`, `SetCompetitionCategories`) — reviewers should expect the same shape for
  any future full-row-edit endpoint rather than inventing a PATCH-style partial update.
- `EntryInstructions` sets a precedent: any future judge-facing field addition must be justified
  against BR-01/FR-019 explicitly (as this one is here), not added by copying an existing
  projection without re-checking which fields it selects.
