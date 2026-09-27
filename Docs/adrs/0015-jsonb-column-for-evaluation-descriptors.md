# 0015 - jsonb column for structured tasting-sheet descriptors, not new relational columns

**Status:** Accepted
**Date:** 2026-09-21

## Context

The organizer asked the judge sheet to mirror a reference paper BJCP sheet: per-attribute
descriptors (4-stop intensity sliders, bipolar sliders, closed-list Color/Clarity/Foam with
"other" text, a 20-term off-flavor checklist, an "inappropriate for style" flag per rated
attribute) plus a holistic feedback box, **persisted as real data**. That is ~30 optional fields in
five nested shapes. They are always read whole (review, organizer drill-down, PDF), never queried
per field, and never feed the scores. The five capped scores and comments stay unchanged.

## Decision

- `Evaluation.DescriptorsJson` (`jsonb`) holds a `System.Text.Json`-serialized
  `EvaluationDescriptorsDto` (camelCase, same shape on the wire and in storage), following the
  existing `AuditLog.DataJson` / `DispatchJob.PayloadJson` pattern.
- `Evaluation.FeedbackComment` is a plain `varchar(4000)` column.
- One shared `EvaluationDescriptorsDtoValidator` (closed lists from `EvaluationDescriptorCatalog`,
  500-char cap on free text, ≤ 20 off-flavors) used by submit and correction. This deliberately
  differs from the per-command duplication of score/comment rules.
- Hardening from PR #45 review: deserialization failures return `null` instead of throwing (so a
  bad row cannot break the drill-down or PDF generation); organizer corrections keep existing
  descriptors/feedback when the request omits them; the frontend sends `null` for untouched
  sliders instead of a fabricated default.
- The judge's discrepancy adjustment does not accept descriptors.

## Consequences

- One migration; the descriptor shape can evolve without further migrations.
- Descriptors are opaque to SQL (no per-field index or filter). If a future story needs
  descriptor-level reporting, revisit: jsonb expression indexes, EF `OwnsOne().ToJson()`, or
  promoting key fields to columns.
