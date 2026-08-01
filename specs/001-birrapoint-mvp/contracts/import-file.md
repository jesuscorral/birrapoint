# Import File Contract: Beer Entry `.xlsx` (ACCE format)

**Consumed by**: `POST /api/v1/competitions/{id}/imports` ([rest-api.md](./rest-api.md)) ·
**Parser**: first worksheet only; row 1 is the header; parsing stops at the first fully empty row.

This is the real club (ACCE) entry-form export: Spanish headers, typed Excel date/number cells
(not text), and no beer-name column — the organizer types one in later if they want one at all.

## Columns

Header names are matched case-insensitively, trimmed; column order is not significant.

| # | Header | Required | Type / Constraints | Maps to |
|---|--------|----------|--------------------|---------|
| 1 | `Marca temporal` | yes | date/time | BeerEntry.SubmittedAt |
| 2 | `Dirección de correo electrónico` | yes | valid email ≤ 320; participants deduplicated per competition by this value | Participant.Email |
| 3 | `Numero socio ACCE` | no | numeric or text ≤ 50 (stored as plain digits, no decimal/scientific-notation artifact) | Participant.AcceMemberNumber |
| 4 | `Nombre y apellidos` | yes | text ≤ 200 | Participant.Name |
| 5 | `Fecha de nacimiento` | no | date | Participant.DateOfBirth |
| 6 | `Teléfono` | no | numeric or text ≤ 30 (stored as plain digits) | Participant.Phone |
| 7 | `Categoria` | yes | must exact-match (case-insensitive, trimmed) one of this competition's own `CompetitionCategory.Name` values (wizard step 3) | BeerEntry.CompetitionCategoryId |
| 8 | `Estilo` | yes | BJCP 2021 style — split on the first `". "`; if the prefix matches a catalog **code** use it, else fall back to an exact code-or-name match on the whole cell (so `"21C. Hazy IPA"`, bare `"21C"`, and bare `"American IPA"` all work) | BeerEntry.StyleCode |
| 9 | `Grado alcohol: (%)` | yes | numeric, 0–99.99 | BeerEntry.AbvPercent (`decimal(4,2)`) |
| 10 | `Número de botellas enviadas` | — | **ignored entirely** — read by nobody, stored nowhere | — |
| 11 | `Fecha de elaboración` | no | date | BeerEntry.BrewDate |
| 12 | `Fecha de embotellado` | no | date | BeerEntry.BottlingDate |
| 13 | `Maltas utilizadas` | no | free text ≤ 1000, stored verbatim | BeerEntry.Malts |
| 14 | `Lupulos utilizados` | no | free text ≤ 1000, stored verbatim | BeerEntry.Hops |
| 15 | `Levadura utilizada` | no | free text ≤ 1000, stored verbatim | BeerEntry.Yeast |
| 16 | `Otros ingredientes` | no | free text ≤ 1000, stored verbatim | BeerEntry.OtherIngredients |
| 17 | `Instrucciones de entrada` | no | free text ≤ 1000 — **judge-facing** (the one deliberate exception to BR-01/FR-019 alongside BlindCode/StyleCode) | BeerEntry.EntryInstructions |

There is no beer-name column. `BeerEntry.BeerName` is always `null` coming out of import; the
organizer may type one in via the full row edit (below) or the post-consolidation entry editor, or
leave it `null` indefinitely — blind code + style + category already fully identify the sample.

## Row validation outcomes

| Status | Condition | Resolution path |
|--------|-----------|-----------------|
| `Valid` | all required cells present and well-formed; category and style both resolve, and the style is assigned to that category (FR-053) | imported on consolidation |
| `CategoryMismatch` | row well-formed but `Categoria` doesn't exact-match any of this competition's categories | Mapping & Correction screen: full row edit setting `competitionCategoryId`, or exclude |
| `StyleMismatch` | row well-formed but `Estilo` doesn't resolve to a catalog code (FR-010) | full row edit setting `styleCode`, or exclude (FR-011) |
| `CategoryStyleMismatch` | row well-formed, category and style both individually resolve, but the resolved style isn't assigned to the resolved category under this competition's FR-052 configuration | full row edit picking a compatible category/style pair, or fix the assignment in wizard step 3 and revalidate (FR-054), or exclude |
| `Invalid` | missing required cell, bad email, malformed date, out-of-range/unparsable number, over-length value (row error message included) | full row edit correcting the field(s), or exclude — a style/category id cannot repair a malformed/missing required cell, so those alone don't move an `Invalid` row to `Valid` |
| `Excluded` | organizer resolved a `CategoryMismatch`/`StyleMismatch`/`Invalid` row via `POST .../rows/{rowNumber}/exclude` | terminal for this row; counted in `consolidate`'s `excluded` total, never imported; cannot be edited afterward |

File-level rejections (`400 invalid-import-file`): not an `.xlsx`, no worksheet, missing required
header columns, zero data rows.

## Full row edit

`PUT /api/v1/competitions/{id}/imports/{importId}/rows/{rowNumber}` is a full-replace edit of every
organizer-editable field (participant name/email/ACCE#/DOB/phone, `competitionCategoryId`,
`styleCode`, `submittedAt`, `abvPercent`, brew/bottling dates, malts/hops/yeast/other ingredients,
`entryInstructions`, `beerName`). `competitionCategoryId` (if given) must belong to this
competition; `styleCode` (if given) must exist in the BJCP 2021 catalog — either check failing is a
`400 invalid-import-file`. Both may be left `null`/absent, in which case the row simply stays
unresolved. After saving, `status` recomputes to `Valid` once participant email is well-formed,
`competitionCategoryId` is set, `styleCode` is set, and the style is assigned to that category
(FR-053) — `CategoryStyleMismatch` if the first three hold but the pair isn't allowed, `Invalid`
otherwise. Editing an `Excluded` row is rejected with `400 invalid-import-file`.

## Revalidation

`POST /api/v1/competitions/{id}/imports/{importId}/revalidate` (FR-054) re-runs the category/
style/allow-list resolution above for every row currently `Valid`/`CategoryMismatch`/
`StyleMismatch`/`CategoryStyleMismatch` (skips `Invalid` — its cause is unrelated to categories —
and `Excluded`, which is terminal), against the competition's **current** categories and style
assignments. Exists because `PUT /competitions/{id}/categories` (FR-052) is a full-replace: it
deletes and recreates `CompetitionCategory`/`CompetitionCategoryStyle` rows, so an already-resolved
`competitionCategoryId` on a staged row can go stale (point at a deleted id) even when a
same-named category still exists. Per row: the currently-resolved category/style are kept if still
valid, otherwise re-matched from the row's original raw `Categoria`/`Estilo` cell text; the pair is
then re-checked against the allow-list. Lets the organizer fix a category/style-assignment problem
in wizard step 3 and pick the import back up without re-uploading the file — the frontend calls
this automatically when the Mapping & Correction screen is reopened with a pending import already
in progress. No-op once the batch is `Consolidated`.

## Semantics

- Re-importing while a previous import of the same competition is unconsolidated → previous
  pending import is discarded (single active import per competition).
- Blind codes are **not** part of the file — generated at consolidation (FR-013).
- On consolidation, a row's `ParticipantEmail` matching an existing `Participant` **within this
  same competition** updates that Participant's Name/AcceMemberNumber/DateOfBirth/Phone from the
  row's current values (last-import-wins); it never creates a duplicate. A competition is always
  its own scope — the same email in a different competition gets its own independent `Participant`
  row.
- `ParticipantEmail` is the COI matching key against judge emails (FR-017); this format has no
  collaborators column, so `EntryCollaborator` is never populated by import (collaborators, where
  used, are assigned some other way, out of this contract's scope).

## Example

Real organizer worksheet row (typed Excel cells, not text):

| Marca temporal | Dirección de correo electrónico | Numero socio ACCE | Nombre y apellidos | Fecha de nacimiento | Teléfono | Categoria | Estilo | Grado alcohol: (%) | Número de botellas enviadas | Fecha de elaboración | Fecha de embotellado | Maltas utilizadas | Lupulos utilizados | Levadura utilizada | Otros ingredientes | Instrucciones de entrada |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2025-09-01 09:21:16 | dezaprieto@gmail.com | 1423 | José Deza Prieto | 1958-12-05 | 699989612 | Estilos clásicos | 21C. Hazy IPA | 7.6 | 3 | 2025-08-12 | 2025-08-28 | Pale Ale, Trigo, Copos de avena, Melanoidin | Amarillo, Citra, Mosaic | White Lab WL-001-P California Ale | | |
