# Import File Contract: Beer Entry `.xlsx`

**Consumed by**: `POST /api/v1/competitions/{id}/imports` ([rest-api.md](./rest-api.md)) ·
**Parser**: first worksheet only; row 1 is the header; parsing stops at the first fully empty row.

## Columns

Header names are matched case-insensitively, trimmed; column order is not significant.

| # | Header | Required | Type / Constraints | Maps to |
|---|--------|----------|--------------------|---------|
| 1 | `ParticipantName` | yes | text ≤ 200 | Participant.Name |
| 2 | `ParticipantEmail` | yes | valid email ≤ 320; participants deduplicated per competition by this value | Participant.Email |
| 3 | `BeerName` | yes | text ≤ 200 | BeerEntry.BeerName |
| 4 | `Style` | yes | BJCP 2021 style **code** (`21A`) or **exact name** (`American IPA`) | BeerEntry.StyleCode |
| 5 | `Collaborators` | no | semicolon-separated emails; each valid | EntryCollaborator.Email × n |

## Row validation outcomes

| Status | Condition | Resolution path |
|--------|-----------|-----------------|
| `Valid` | all required cells present and well-formed; style matches catalog | imported on consolidation |
| `StyleMismatch` | row well-formed but style doesn't exactly match code or name (FR-010) | Mapping & Correction screen: assign catalog style (moves the row to `Valid`) or exclude (FR-011) |
| `Invalid` | missing required cell, bad email, over-length value (row error message included) | fix in source file and re-upload, or exclude — **not** assign-style: a style code cannot repair a malformed/missing required cell, so `assign-style` on an `Invalid` row is rejected (`400 invalid-import-file`) |
| `Excluded` | organizer resolved a `StyleMismatch`/`Invalid` row via `action: "exclude"` | terminal for this row; counted in `consolidate`'s `excluded` total, never imported |

File-level rejections (`400 invalid-import-file`): not an `.xlsx`, no worksheet, missing required
header columns, zero data rows.

## Semantics

- Duplicate `(ParticipantEmail, BeerName)` pairs within one file → second occurrence is `Invalid`
  (duplicate entry).
- Re-importing while a previous import of the same competition is unconsolidated → previous
  pending import is discarded (single active import per competition).
- Blind codes are **not** part of the file — generated at consolidation (FR-013).
- `ParticipantEmail` and `Collaborators` are the COI matching keys against judge emails (FR-017,
  FR-018); this is why they are mandatory in the contract.

## Example

| ParticipantName | ParticipantEmail | BeerName | Style | Collaborators |
|---|---|---|---|---|
| Ana Gómez | ana@brew.example | Hop Cannon | 21A | luis@brew.example |
| Luis Pérez | luis@brew.example | Dark Matter | Imperial Stout | |
| Sam Roe | sam@brew.example | Fizzy Lifting | 99Z *(→ StyleMismatch)* | |
