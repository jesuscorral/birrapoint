# 0032 - Unified import feature

**Status:** Accepted
**Date:** 2026-11-01

## Context
Two organizer import flows exist: beer entries (`.xlsx` ACCE format, FR-011) and the judge roster (US14, FR-055). Beer import owned the generic name `Import` (`/imports`, `ImportBatch`, `WorkbookParser`), while the judge roster lived in `Features/Judges/` under `/judge-imports`. The two flows share one shape (upload, row edit, exclude, consolidate), so the split location and the ambiguous "Import" name made the code and API hard to read. ADR-0031 left the judge-import slice flat as a known exception.

## Decision
- One feature folder, `Features/Import/`, in the ADR-0031 layout. `BaseImportEndpoints.cs` maps both route groups (tags `BeerImport`, `JudgeImport`).
- Routes (breaking, no aliases; same precedent as ADR-0030): `/api/v1/competitions/{id}/imports/beers/...` (was `/imports/...`) and `/imports/judges/...` (was `/judge-imports/...`). Sub-paths, response shapes, auth and errors are unchanged.
- Beer-side types are renamed with a `Beer` prefix: `BeerImportBatch`, `BeerImportRow`, their status enums and DTOs, `BeerWorkbookParser`, slices `*BeerImport*`. Judge-side types keep the `Judge` prefix and move from `Features/Judges/`.
- The EF tables stay `ImportBatches`/`ImportRows` through explicit `ToTable`. The model snapshot is updated; no migration is generated.
- `ProvisionJudgeAccountHandler`, `SendInvitationHandler` and `JudgeSkipDto` stay in `Features/Judges/`.

## Consequences
- Beer and judge imports are distinguishable by name, route and folder; the ADR-0031 exception is closed.
- Breaking API change for any client of the old paths. Acceptable pre-release; the wizard is the only consumer and is updated in the same change.
- Existing migrations still name the old CLR types (`ImportBatch`/`ImportRow`). This is harmless: migrations are history and the snapshot is authoritative.
- Completed `tasks.md` entries keep their old paths (see the note there).
