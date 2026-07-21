# REST API Contract: BirraPoint MVP

**Base path**: `/api/v1` · **Auth**: Bearer JWT (Keycloak) on every endpoint — deny-by-default;
role requirement listed per endpoint. · **Errors**: RFC 7807 `application/problem+json` with
stable `type` URNs (see [Error catalog](#error-catalog)). · **OpenAPI**: served at `/openapi` by
the running API; this document is the source contract.

Conventions: `404` for resources outside the caller's scope (never reveal existence);
`400` for validation failures (field map in `errors`); `409` for domain/state conflicts.

## Catalog

| Method & Path | Role | Description |
|---|---|---|
| `GET /styles` | any authenticated | BJCP 2021 catalog, lightweight (import matching / searchable picker). `200` → `[{ code, name, categoryNumber, categoryName }]` |
| `GET /styles/{code}` | any authenticated | Full BJCP 2021 style detail incl. vital statistics and guide description, for the judge-facing evaluation-sheet reference panel (FR-049). `200` → `{ code, name, categoryNumber, categoryName, vitalStatistics: { ogLow, ogHigh, fgLow, fgHigh, ibuLow, ibuHigh, srmLow, srmHigh, abvLow, abvHigh }, description: { overallImpression, aroma, appearance, flavor, mouthfeel, comments, history, characteristicIngredients, styleComparison, entryInstructions, commercialExamples: [], tags: [] } }`. `404` if code not in catalog. |

## Competitions

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions` | ORGANIZER | Create draft. Body: `{ name, venue, startDate, endDate, description?, logoUrl?, entryLimit?, registrationStart?, registrationEnd? }` → `201` `{ id, state: "Draft", … }`. `400` on missing required field / endDate < startDate / registrationEnd < registrationStart. |
| `GET /competitions` | ORGANIZER | Own competitions summary list (US13): `[{ id, name, venue, startDate, endDate, state }]`, `state` one of `Draft`\|`Active`\|`InEvaluation`\|`Finalized` (FR-050). Already implemented (T027); the organizer dashboard is its first consumer. |
| `GET /competitions/{id}` | ORGANIZER | Full detail. |
| `PUT /competitions/{id}` | ORGANIZER | Update wizard data. Allowed in `Draft`/`Active` only → else `409 invalid-state-transition`. |
| `POST /competitions/{id}/state` | ORGANIZER | Body: `{ target: "Active" \| "InEvaluation" \| "Finalized" }`. Forward-only, skip-free (FR-006) → `409 invalid-state-transition` otherwise. `Finalized` additionally requires all tables closed → `409 tables-still-open` (lists open table ids). On success `200 { state }`; `Finalized` enqueues the dispatch pipeline (FR-036). |

## Entry Import

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions/{id}/imports` | ORGANIZER | Multipart `.xlsx` upload (schema: [import-file.md](./import-file.md)). Competition must be `Draft`/`Active` (`409 invalid-state-transition` otherwise). → `201` `{ importId, rows: [{ rowNumber, status: "Valid" \| "StyleMismatch" \| "Invalid", data, error? }] }`. Malformed/empty file → `400 invalid-import-file`. A prior unconsolidated batch for the competition is discarded (single active batch). |
| `GET /competitions/{id}/imports/{importId}` | ORGANIZER | Current row states — `status` may additionally be `Excluded` for rows resolved via `action: "exclude"` (see below). |
| `PUT /competitions/{id}/imports/{importId}/rows/{rowNumber}` | ORGANIZER | Resolve a row: `{ action: "assign-style", styleCode }` (only valid for a `StyleMismatch` row — `400 invalid-import-file` on an `Invalid` row, since a style code can't fix a missing/malformed required cell) or `{ action: "exclude" }` (valid for either `StyleMismatch` or `Invalid`, sets `status: "Excluded"`). `400` if styleCode not in catalog. |
| `POST /competitions/{id}/imports/{importId}/consolidate` | ORGANIZER | `409 unresolved-import-rows` while any row is `StyleMismatch`/`Invalid` (FR-011). `409 invalid-state-transition` if the batch was already consolidated (no re-consolidation). On success `200` `{ imported, excluded, entries: [{ id, blindCode, styleCode }] }` — blind codes generated here (FR-013); `excluded` counts `Excluded` rows. |

## Entries (organizer)

| Method & Path | Role | Description |
|---|---|---|
| `GET /competitions/{id}/entries` | ORGANIZER | Every entry in the competition with its current table assignment — feeds the table-setup UI's "Unassigned" column (T048). `200` → `[{ id, blindCode, styleCode, styleName, abvLow, abvHigh, beerName, notValidForBos, tastingTableId, tastingTableName }]`; `tastingTableId`/`tastingTableName` are `null` until the entry is assigned via `POST`/`PUT .../tables`. Organizer-only, so unlike judge-facing DTOs this includes `beerName` (no BR-01 concern). |

## Judges

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions/{id}/judges` | ORGANIZER | Body: `{ emails: [string] }`. Creates missing profiles, queues invitations (Keycloak provisioning happens per-delivery-attempt inside the async `SendInvitation` job, not synchronously in this request). → `201` `{ created: [{ id, email }], skipped: [{ email, reason: "duplicate-in-list" \| "already-registered" }] }` (FR-014/FR-015). |
| `GET /competitions/{id}/judges` | ORGANIZER | `[{ id, email, displayName, invitationStatus, attempts, lastError, sentAt }]`. |
| `PUT /competitions/{id}/judges/{judgeId}` | ORGANIZER | Correct a judge's email before first login (edge case: bounced invitation). Body: `{ email }`. Re-validates uniqueness (FR-015), updates the Keycloak account. `409 judge-already-active` once the judge has authenticated. **COI matching / BOS re-flagging against the new address (FR-017/FR-018) is still not implemented here** — `Features/Tables` now exists (Phase 7), so the blocking dependency is resolved, but wiring this endpoint to `CoiDetector`/`BosFlagRules` was never in either phase's task scope; tracked as an explicit follow-up (see `Docs/arquitectura_viva.md` Recorded debt). |
| `POST /competitions/{id}/judges/{judgeId}/invitation` | ORGANIZER | Re-send invitation (edge case: bounced email after correction). |

## Tables (organizer)

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions/{id}/tables` | ORGANIZER | Body: `{ name, judgeIds: [], beerEntryIds: [] }`. Competition must be `Draft`/`Active` (`409 invalid-state-transition` otherwise, same gate as `POST .../imports` — data-model.md §Competition state gates). COI violation → `409 conflict-of-interest` `{ conflicts: [{ judgeId, beerEntryIds }] }`, nothing persisted (FR-017). Success → `201`; response includes `bosFlaggedEntryIds` when FR-018 fired. |
| `PUT /competitions/{id}/tables/{tableId}` | ORGANIZER | Same body, COI semantics, and `Draft`/`Active` state gate as `POST`. `409 table-closed` if the table itself is closed. |
| `GET /competitions/{id}/tables` | ORGANIZER | Tables with judges, samples, progress, state. |
| `DELETE /competitions/{id}/tables/{tableId}/judges/{judgeId}` | ORGANIZER | Live removal (FR-039). `200`; sets `RemovedAt`, revokes access, emits `JudgeRemoved`, audit-logged. Already-submitted evaluations stay valid. |

## Judge workspace

All endpoints below return `404` for tables/samples the caller is not assigned to. Payloads are
built exclusively from the blind projection (`JudgeSampleDto`) — see data-model.md §Anonymity.

| Method & Path | Role | Description |
|---|---|---|
| `GET /me/tables` | JUDGE | Assigned tables: `[{ tableId, name, competitionState, tableState, orderFixed, orderFixedBy? }]`. Competition must be `Active`+ (invisible in `Draft`). |
| `GET /me/tables/{tableId}/samples` | JUDGE | `[JudgeSampleDto]` = `{ beerEntryId, blindCode, styleCode, styleName, sequenceOrder?, evaluationStatus: "NotStarted" \| "Submitted" \| "PendingConsensus" }`. |
| `POST /me/tables/{tableId}/order` | JUDGE | Body: `{ orderedBeerEntryIds: [] }` (must be a permutation of the table's samples → `400`). One-shot: `409 order-already-fixed` `{ fixedBy }` if raced (US6-4). Requires competition `Active`/`InEvaluation`. Emits `TableOrderFixed`. |
| `POST /me/tables/{tableId}/evaluations` | JUDGE | Header `X-Idempotency-Key: {competitionId}:{tableId}:{judgeId}:{entryId}`. Body: `{ beerEntryId, scores: { aroma, appearance, flavor, mouthfeel, overall }, comments: { aroma, appearance, flavor, mouthfeel, overall } }`. Validation (FR-023/FR-025): caps 12/3/20/5/10, comments ≥ 20 chars → `400`. Preconditions: competition `InEvaluation` (`409 invalid-state-transition`), order fixed (`409 order-not-fixed`), sample is next in sequence (`409 out-of-sequence`), table open (`409 table-closed`). Success → `201` `{ evaluationId, status: "Confirmed" \| "PendingConsensus", total, discrepancy? }`. Idempotent replay → `200` with the stored result (R-07). |
| `PUT /me/tables/{tableId}/evaluations/{evaluationId}` | JUDGE | Allowed **only** while an open discrepancy covers it and caller is involved → else `409 evaluation-locked` (Clarification Q2). Re-runs discrepancy check; may resolve the alert. |
| `GET /me/tables/{tableId}/discrepancies` | JUDGE | Open alerts involving the caller: `[{ alertId, blindCode, totals: [{ judgeDisplayName, total, isMine }] }]`. |
| `POST /me/tables/{tableId}/close` | JUDGE | Close table (FR-033). `409 evaluations-incomplete` `{ missing: [...] }` or `409 discrepancy-open` `{ blindCodes }`. Success `200`; emits `TableClosed`; permanent. |

## Monitoring & audit (organizer)

| Method & Path | Role | Description |
|---|---|---|
| `GET /competitions/{id}/progress` | ORGANIZER | Initial dashboard state: `[{ tableId, name, state, completed, expected, percent }]` (deltas then arrive via SignalR). |
| `GET /competitions/{id}/entries/{entryId}/evaluations` | ORGANIZER | Audit drill-down, read-only: full evaluations incl. judge names + consolidated mean when table closed (FR-038, FR-042). |
| `PUT /competitions/{id}/evaluations/{evaluationId}` | ORGANIZER | Post-close correction (FR-035). Body: same scores/comments shape as judge submission; same caps/length validation. Recomputes total + consolidated mean; writes AuditLog (author, timestamp, before/after). Allowed regardless of table state; organizer-only. |

## Results & dispatch (organizer)

| Method & Path | Role | Description |
|---|---|---|
| `GET /competitions/{id}/results/archive` | ORGANIZER | `200` ZIP stream (`/CompetitionName/ParticipantID/Style_BlindCode.pdf`, FR-040) or `202 { status }` while generation is in progress. |
| `GET /competitions/{id}/dispatch` | ORGANIZER | Per-participant email status: `[{ participantId, email, status, attempts, lastError? }]` (FR-041). |
| `POST /competitions/{id}/dispatch/retries` | ORGANIZER | Body: `{ participantIds: [] }` → re-queue failed result emails. |

## Error catalog

| `type` URN (`urn:birrapoint:…`) | HTTP | Raised by |
|---|---|---|
| `validation` | 400 | any FluentValidation failure (`errors` field map) |
| `invalid-import-file` | 400 | unreadable/empty/mis-schema’d upload |
| `invalid-state-transition` | 409 | FR-006 gates (skip/reverse/edit in wrong state) |
| `conflict-of-interest` | 409 | FR-017 table assignment |
| `unresolved-import-rows` | 409 | FR-011 consolidation |
| `order-already-fixed` | 409 | US6-4 race |
| `order-not-fixed` | 409 | FR-022 precondition |
| `out-of-sequence` | 409 | FR-022 sequence enforcement |
| `evaluation-locked` | 409 | Clarification Q2 (edit outside discrepancy) |
| `table-closed` | 409 | FR-034 (incl. late offline syncs) |
| `evaluations-incomplete` | 409 | FR-033 close precondition |
| `discrepancy-open` | 409 | FR-032 close precondition |
| `tables-still-open` | 409 | FR-036 finalize precondition |
| `judge-already-active` | 409 | judge email correction after first login |

Breaking changes to any of the above require a spec amendment and a `/api/v2` decision
(Constitution Principle VI).
