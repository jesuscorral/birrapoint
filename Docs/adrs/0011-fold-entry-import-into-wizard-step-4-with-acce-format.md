# 0011 - Fold entry import into wizard step 4, replace with the ACCE spreadsheet format

**Status:** Accepted
**Date:** 2026-07-31

## Context

The original import used an invented English-header format, was not linked from the UI, and only
allowed correcting the style cell. The organizer's real club (ACCE) spreadsheet has Spanish
headers, brewer contact and membership fields, a `Categoria` that must match the competition's own
categories, `Estilo` as `"<code>. <name>"`, no beer name, and full recipe data. Every field must be
reviewable before consolidation.

## Decision

1. **Replace the format**: `WorkbookParser` supports only the ACCE columns
   (`contracts/import-file.md`). Nothing depended on the old one.
2. **`BeerEntry.BeerName` is optional**; the organizer may type one during review.
3. **`Participant` stays per competition** (`(CompetitionId, Email)`); a repeated email within an
   import updates the row (last import wins).
4. **New `CategoryMismatch` row status**: `Categoria` must match a `CompetitionCategory` name
   (case-insensitive) or the row blocks consolidation.
5. **Full-row edit `PUT`** replaces the narrow `assign-style` action; `exclude` stays a separate
   one-way endpoint.
6. **`EntryInstructions` is the single deliberate exception to blind anonymity** (FR-019): judges
   see it because it carries serving instructions. Beer name and every participant field remain
   excluded from anything a judge reads.
7. **Import is wizard step 4**, styled like the rest of the wizard.

## Consequences

- One additive migration (wider `Participant`/`BeerEntry`/`ImportRow`, required
  `BeerEntry.CompetitionCategoryId`).
- Another club's template would need format detection or a parallel parser (out of scope).
- Full-row edits follow the existing full-replace `PUT` convention.
- Any future judge-facing field must be justified against FR-019 explicitly, as here.
