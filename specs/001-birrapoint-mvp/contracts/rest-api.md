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
| `GET /styles` | any authenticated | BJCP 2021 catalog. `200` → `[{ code, name, categoryNumber, categoryName }]` |

## Competitions

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions` | ORGANIZER | Create draft. Body: `{ name, venue, startDate, endDate, description?, logoUrl?, entryLimit?, registrationDeadline? }` → `201` `{ id, state: "Draft", … }`. `400` on missing required field / endDate < startDate. |
| `GET /competitions` | ORGANIZER | Own competitions summary list. |
| `GET /competitions/{id}` | ORGANIZER | Full detail. |
| `PUT /competitions/{id}` | ORGANIZER | Update wizard data. Allowed in `Draft`/`Active` only → else `409 invalid-state-transition`. |
| `POST /competitions/{id}/state` | ORGANIZER | Body: `{ target: "Active" \| "InEvaluation" \| "Finalized" }`. Forward-only, skip-free (FR-006) → `409 invalid-state-transition` otherwise. `Finalized` additionally requires all tables closed → `409 tables-still-open` (lists open table ids). On success `200 { state }`; `Finalized` enqueues the dispatch pipeline (FR-036). |

## Entry Import

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions/{id}/imports` | ORGANIZER | Multipart `.xlsx` upload (schema: [import-file.md](./import-file.md)). Competition must be `Draft`/`Active`. → `201` `{ importId, rows: [{ rowNumber, status: "Valid" \| "StyleMismatch" \| "Invalid", data, error? }] }`. Malformed/empty file → `400 invalid-import-file`. |
| `GET /competitions/{id}/imports/{importId}` | ORGANIZER | Current row states. |
| `PUT /competitions/{id}/imports/{importId}/rows/{rowNumber}` | ORGANIZER | Resolve a row: `{ action: "assign-style", styleCode }` or `{ action: "exclude" }`. `400` if styleCode not in catalog. |
| `POST /competitions/{id}/imports/{importId}/consolidate` | ORGANIZER | `409 unresolved-import-rows` while any row is `StyleMismatch`/`Invalid` (FR-011). On success `200` `{ imported, excluded, entries: [{ id, blindCode, styleCode }] }` — blind codes generated here (FR-013). |

## Judges

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions/{id}/judges` | ORGANIZER | Body: `{ emails: [string] }`. Creates missing profiles, provisions Keycloak users, queues invitations. → `201` `{ created: [...], skipped: [{ email, reason: "duplicate-in-list" \| "already-registered" }] }` (FR-014/FR-015). |
| `GET /competitions/{id}/judges` | ORGANIZER | Profiles + invitation delivery status. |
| `POST /competitions/{id}/judges/{judgeId}/invitation` | ORGANIZER | Re-send invitation (edge case: bounced email after correction). |

## Tables (organizer)

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions/{id}/tables` | ORGANIZER | Body: `{ name, judgeIds: [], beerEntryIds: [] }`. COI violation → `409 conflict-of-interest` `{ conflicts: [{ judgeId, beerEntryIds }] }`, nothing persisted (FR-017). Success → `201`; response includes `bosFlaggedEntryIds` when FR-018 fired. |
| `PUT /competitions/{id}/tables/{tableId}` | ORGANIZER | Same body & COI semantics. `409 table-closed` if closed. |
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

Breaking changes to any of the above require a spec amendment and a `/api/v2` decision
(Constitution Principle VI).
