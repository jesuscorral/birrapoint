# Import File Contract: Judge Roster `.xlsx`

**Consumed by**: `POST /api/v1/competitions/{id}/judge-imports` ([rest-api.md](./rest-api.md)) ·
**Parser**: first worksheet only; row 1 is the header; parsing stops at the first fully empty row.

This is the organizer's own club roster export: Spanish headers, one row per judge. Unlike the
beer-entry format ([import-file.md](./import-file.md)), no cell resolves against any catalog — BJCP
rank, BJCP ID, and preferred category are all stored verbatim as free text; only name and email are
required.

## Columns

Header names are matched case-insensitively, trimmed; column order is not significant.

| # | Header | Required | Type / Constraints | Maps to |
|---|--------|----------|--------------------|---------|
| 1 | `Nombre y apellidos` | yes | text ≤ 200 | Judge.DisplayName |
| 2 | `Correo electrónico` | yes | valid email ≤ 320; judges deduplicated per competition by this value (same as manually-registered judges, FR-015/FR-058) | Judge.Email |
| 3 | `Rango BJCP` | no | free text ≤ 100, stored verbatim — club vocabulary (e.g. "Certificado", "Reconocido", "Pendiente de Rango"), not a controlled catalog | Judge.BjcpRank |
| 4 | `BJCP ID` | no | free text ≤ 50, stored verbatim — observed formats include `E####`, bare numeric, and the placeholder `Pte` ("not yet assigned"); never parsed or validated | Judge.BjcpId |
| 5 | `Categoría preferida` | no | free text ≤ 200, stored verbatim; informational only, not cross-checked against this competition's own `CompetitionCategory` names | Judge.PreferredCategory |
| 6 | `Preferencias` | no | free text ≤ 2000, stored verbatim as plain text — **never interpreted as markup**, even when the source cell contains literal `<br>`-style text (organizer-facing rendering must escape it like any other user-supplied text) | Judge.Preferences |

## Row validation outcomes

| Status | Condition | Resolution path |
|--------|-----------|-----------------|
| `Valid` | `Nombre y apellidos` and `Correo electrónico` both present and well-formed | persisted as a judge profile on consolidation |
| `Invalid` | missing/malformed name or email (row error message included) | Correction screen: full row edit correcting the field(s), or exclude |
| `Excluded` | organizer resolved an `Invalid` row via `POST .../rows/{rowNumber}/exclude` | terminal for this row; counted in `consolidate`'s `excluded` total, never imported; cannot be edited afterward |

File-level rejections (`400 invalid-import-file`): not an `.xlsx`, no worksheet, missing required
header columns, zero data rows.

## Full row edit

`PUT /api/v1/competitions/{id}/judge-imports/{importId}/rows/{rowNumber}` is a full-replace edit of
every field (`name`, `email`, `bjcpRank`, `bjcpId`, `preferredCategory`, `preferences`). After
saving, `status` recomputes to `Valid` once `name` and `email` are both present and well-formed,
`Invalid` otherwise. Editing an `Excluded` row is rejected with `400 invalid-import-file`.

There is no revalidation endpoint — unlike the beer-entry import, no field here resolves against
data that can change out from under a pending batch (no category/style allow-list involved), so
there is nothing for a revalidation pass to re-check.

## Semantics

- Re-importing while a previous judge-roster import of the same competition is unconsolidated →
  previous pending import is discarded (single active judge-roster import per competition,
  independent of the beer-entry import's own single-active-batch rule).
- On consolidation (FR-057), a row's `Email` matching an existing `Judge` **within this same
  competition** updates that judge's `BjcpRank`/`BjcpId`/`PreferredCategory`/`Preferences` from the
  row's current values (last-import-wins); it never creates a duplicate profile or account.
  Duplicate emails within the same file resolve to a single upsert, and every earlier same-email
  row is reported in the consolidate response's `skipped` list (`reason: "duplicate-in-list"`,
  FR-058 — same policy/shape as the plain email-list judge registration's `skipped`). A competition
  is always its own scope — the same email already registered as a judge in a *different*
  competition is unaffected and gets its own independent `Judge` row here.
- Consolidation enqueues `ProvisionJudgeAccount` (R-20) for every newly-created judge, and for an
  updated judge **only while that judge's invitation is still `Pending`** — once a password has
  already been issued or attempted (`Invitation.Status` is `Sent`/`Failed`, e.g. after a re-import
  following the organizer's "Notify judges" action), re-provisioning is a no-op: it would reset a
  live or already-delivered credential with no recovery path, so it is skipped rather than
  resetting the judge's password again. No invitation email is ever sent from consolidation — that
  is the separate, explicit "Notify judges" action (`POST /competitions/{id}/judges/notify`,
  FR-059).
- No blind codes, styles, or categories are involved — this format is unrelated to beer-entry
  anonymity concerns (BR-01/FR-019); judge roster data is organizer-only, never judge-facing.

## Example

| Nombre y apellidos | Correo electrónico | Rango BJCP | BJCP ID | Categoría preferida | Preferencias |
|---|---|---|---|---|---|
| Ana García Ruiz | ana.garcia@example.com | Certificado | E4612 | Estilos Clásicos | |
| Luis Martín Soto | luis.martin@example.com | Pendiente de Rango | 10649 | Estilos Clásicos | Me gustaría compartir mesa con Pablo. <br>Mi pareja se ofrece para ayudar. <br>Un saludo. |
| Carmen López Díaz | carmen.lopez@example.com | Pendiente de Rango | Pte | | |
