# 0010 - Store generated score-sheet PDFs and the results ZIP as Postgres `bytea`

**Status:** Accepted
**Date:** 2026-07-23

## Context

Results dispatch (US10) generates one PDF per entry, bundles a per-competition ZIP and emails each
participant their PDFs, in separate background jobs; the ZIP can be downloaded much later. The
approved stack has no blob storage (PostgreSQL is the only server-side store), and adding one just
for a few MB per competition would need its own justification (Principle V).

## Decision

Two entities with `bytea` columns:

- `GeneratedScoreSheet(BeerEntryId, PdfBytes, GeneratedAt)` — upserted per entry, so retries never
  duplicate.
- `ResultsArchive(CompetitionId, ZipBytes, GeneratedAt)` — upserted per competition.

`GeneratePdfs` writes sheets, `BundleZip` builds the archive from them, and `SendResultEmail` and
`GET …/results/archive` only read stored bytes.

## Consequences

- No new infrastructure; `data-model.md` amended.
- Sizes (KB per sheet, a few MB per ZIP) are well within `bytea`/TOAST limits at MVP volumes.
- Both entities are internal, so moving them to blob storage later is a contained change with no
  contract impact.
