# 0015 - jsonb column for structured tasting-sheet descriptors, not new relational columns

**Status:** Accepted
**Date:** 2026-09-21

## Context

The judge evaluation sheet (`Evaluation` entity, FR-023/FR-024/FR-025) has always scored a beer on
exactly five sections — Aroma/Appearance/Flavor/Mouthfeel/Overall — each holding one capped
integer score and one ≥20-character comment. That data model is a constitutional invariant
(root `CLAUDE.md` §6) and stays completely unchanged by this ADR.

The organizer supplied a reference BJCP-style paper score sheet (VIII Concurso Homebrewer
Córdoba) and asked for the judge-facing sheet to mirror it: per-attribute descriptor controls —
discrete 4-stop intensity sliders (Nada/Bajo/Medio/Alto) for things like Malta/Lúpulos/
Fermentación, continuous bipolar sliders (e.g. Lupulado↔Maltoso, Ejemplo clásico↔No acorde al
estilo), single-choice fields with a closed list plus a free-text "Otros" (Color/Claridad/
Espuma), and a fixed 20-term off-flavor checklist — plus a holistic free-text Feedback box
distinct from the five per-section comments. After an explicit clarifying question, the organizer
confirmed these must be **persisted as real structured data**, not just a frontend composition
aid discarded after submit.

That's roughly 30 new fields across 5 nested shapes. `Evaluation` (`Domain/Evaluation.cs`) is
today a flat entity with no owned types or JSON columns; adding one relational column per
descriptor field would mean ~25-30 new `Evaluations` table columns for data that:

- is optional field-by-field (a judge may fill as few or as many as they like, same as the paper
  form),
- is never queried, filtered, or joined against by any existing or planned slice — it's read back
  whole (the judge's own review step, the organizer's audit drill-down, the results PDF), never
  queried per-field,
- has no cap/validation coupling to `SubmitEvaluationRules`' score caps — it's advisory data
  alongside the authoritative scores, not part of them.

This codebase already has a working, if minimal, precedent for exactly this shape of problem:
`AuditLog.DataJson` and `DispatchJob.PayloadJson` are both plain `string` properties mapped
`.HasColumnType("jsonb")`, populated by hand with `System.Text.Json` (`AuditWriter.cs`,
`DispatchJobQueue.cs`) — no EF Core owned-type/`OwnsOne().ToJson()` native JSON mapping is used
anywhere in this codebase yet.

## Decision

Add two columns to `Evaluation`: `DescriptorsJson` (`string?`, `jsonb`) and `FeedbackComment`
(`string?`, `character varying(4000)` — small and always read/written as a whole, so it stays a
plain column like the five section comments, not folded into the JSON blob). `DescriptorsJson`
holds a `System.Text.Json`-serialized `EvaluationDescriptorsDto` (`Features/Evaluations/
EvaluationDescriptors.cs`) — one record tree with five nested, independently-nullable section
objects (`AppearanceDescriptorsDto`, `AromaDescriptorsDto`, `FlavorDescriptorsDto`,
`MouthfeelDescriptorsDto`, `OverallDescriptorsDto`) plus an `OffFlavors` string list, serialized
with `JsonSerializerDefaults.Web` (camelCase) so the exact same DTO shape round-trips through the
wire and the jsonb column with no re-shaping step (`EvaluationDescriptorsSerializer`). Every
enum-like field (Color, Clarity, Foam, the 20 off-flavor terms) is validated against a closed
list (`EvaluationDescriptorCatalog`) by a new shared `EvaluationDescriptorsDtoValidator` —
unlike `EvaluationScoresDto`/`EvaluationCommentsDto`'s rules, which this codebase deliberately
duplicates per command (`SubmitEvaluationCommandValidator`/`CorrectEvaluationCommandValidator`),
this one is shared via FluentValidation's `SetValidator(...)`, since it's new enough to start
that way rather than carry forward a duplication convention that predates it.

`AdjustEvaluationCommand` (the judge's own discrepancy-adjustment PUT) is deliberately **not**
touched by this change — descriptors on that flow are out of scope for now, a follow-up if the
organizer wants them there too.

## Consequences

- **Positive**: one migration (`AddEvaluationDescriptors`) instead of ~25-30 column-by-column
  ones; the shape can evolve (a new descriptor field, a reordered section) without a migration at
  all, matching how `AuditLog`/`DispatchJob` already handle similarly free-form, whole-record-read
  data in this codebase.
- **Positive**: every descriptor field being independently optional falls out for free from a
  nullable-record-tree JSON shape — no `NOT NULL` column-by-column bookkeeping, no `Validators
  .Required` conflicts with the paper form's own "fill what you like" behavior.
- **Negative**: `DescriptorsJson`'s contents are opaque to Postgres — no per-field index, no
  server-side filtering/aggregation ("show me every evaluation where Malta was rated Alto") is
  possible without either a `jsonb` path query (workable, but this codebase has zero precedent for
  writing one) or deserializing every row application-side. Acceptable today since nothing reads
  descriptors except as a whole per-evaluation blob (review step, drill-down, PDF); revisit if a
  future story needs to query on a specific descriptor across evaluations.
- **Negative**: `EvaluationDescriptorsDtoValidator` being shared (not duplicated like the
  score/comment rules) is a deliberate inconsistency with this codebase's stated convention —
  flagged here so a future reviewer doesn't "fix" it into duplication without reading this ADR
  first; the reasoning is that per-command duplication exists to let two commands' rules drift
  independently on purpose, and there's no case yet where Submit's and Correct's descriptor rules
  should differ.
- **Review trigger**: if a future story needs descriptor-level querying/reporting, revisit whether
  to keep `jsonb` with an expression index, move to `OwnsOne().ToJson()` (EF Core's native JSON
  column mapping, unused anywhere in this codebase today), or normalize the highest-value fields
  into real columns.

## Amendments (senior-code-reviewer, PR #45)

Four gaps found in review, fixed in the same PR, recorded here since they change how this ADR's
own design should be read:

- **Unbounded free text**: the first pass validated closed-list membership and numeric ranges but
  never a string length — `ColorOther`/`FoamOther`/`Texture`/`Notes` were the one input path in
  this codebase that escaped both EF (`jsonb` has no length limit) and FluentValidation, and
  `OffFlavors` had no count cap either. Fixed: 500-char `MaximumLength` on every free-text
  descriptor field (same reasoning as the five section comments' 2000 and `FeedbackComment`'s
  4000 — a cap proportionate to what the field is for), and a count cap on `OffFlavors` (≤ the
  catalog's own 20 terms).
- **`Deserialize` could throw**: a row whose blob doesn't match the current DTO shape (a future
  field rename, a hand-edited row) would 500 the organizer's audit drill-down and poison
  `GeneratePdfsHandler`'s `DispatchJob` retry loop — one bad row failing a whole competition's PDF
  generation, forever. Fixed: `Deserialize` catches `JsonException` and returns `null` ("no
  descriptors recorded") instead of throwing — the same everything-explodes-vs-degrade-gracefully
  choice `EvaluationDescriptorsDto`'s optionality already makes at every other level.
- **`CorrectEvaluation` silently wiped descriptors/feedback**: the request's `Descriptors`/
  `Feedback` default to `null` when omitted, and the handler was unconditionally overwriting the
  columns with whatever the request carried — so an organizer correction posting only `scores`/
  `comments` (the endpoint's own previously-documented minimal body) would null out the judge's
  tasting descriptors as a side effect. Fixed: **preserve-when-absent** — the handler now only
  overwrites `DescriptorsJson`/`FeedbackComment` when the request actually supplies them.
- **Frontend defaulted slider *state* to a concrete value (0/50), not just their display
  position**: this meant every submission — including one where the judge touched zero sliders —
  persisted a full set of fabricated ratings, visible in the organizer's audit view and the
  participant-facing results PDF as if deliberately rated. This is a frontend bug, not a backend
  one, but it directly undermined this ADR's "every descriptor field being independently optional
  falls out for free" claim in practice — the backend accepted `null` correctly the whole time,
  the frontend simply never sent it. Fixed in `evaluation-sheet.component.ts`: slider *state*
  fields stay `null` until touched; the template supplies a separate, display-only fallback for
  where the handle renders before that.
