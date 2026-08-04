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
| `POST /competitions/{id}/state` | ORGANIZER | Body: `{ target: "Active" \| "InEvaluation" \| "Finalized" }`. Forward-only, skip-free (FR-006) → `409 invalid-state-transition` otherwise. `Finalized` additionally requires all tables closed → `409 tables-still-open` (lists open table ids). On success `200 { state }`; `Finalized` enqueues the dispatch pipeline (FR-036). Already implemented (T028); the organizer dashboard's advance-state action (FR-051) is its first UI consumer. |
| `GET /competitions/{id}/categories` | ORGANIZER | Organizer-defined categories grouping the BJCP styles allowed for this competition (FR-052, wizard step 3). `200` → `{ categories: [{ id, name, displayOrder, styleCodes: [] }] }`, ordered by `displayOrder`. `404` if not found/not owned. |
| `PUT /competitions/{id}/categories` | ORGANIZER | Full replace of the competition's categories (same "PUT replaces everything" convention as `PUT /competitions/{id}`). Body: `{ categories: [{ name, displayOrder, styleCodes: [] }] }` → `200` same shape as GET, with server-generated `id`s. A style code may appear in **at most one** category per competition (DB-enforced unique index, also validated at the API boundary) — not every BJCP style needs to be assigned to a category; an unassigned style is simply not allowed in this competition. `400` on: empty `categories`, no style assigned anywhere in the payload, duplicate category name, duplicate/unknown style code. `404` if not found/not owned. `409 invalid-state-transition` outside `Draft`/`Active` (same gate as `PUT /competitions/{id}`). |

## Entry Import

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions/{id}/imports` | ORGANIZER | Multipart `.xlsx` upload of the ACCE club format (schema: [import-file.md](./import-file.md)). Competition must be `Draft`/`Active` (`409 invalid-state-transition` otherwise). → `201` `{ importId, rows: [{ rowNumber, status: "Valid" \| "StyleMismatch" \| "CategoryMismatch" \| "CategoryStyleMismatch" \| "Invalid", data, error? }] }` where `data` is the full editable row shape (participant name/email/ACCE#/DOB/phone, category, style, submittedAt, abvPercent, brew/bottling dates, malts/hops/yeast/other, entryInstructions, beerName). `CategoryStyleMismatch` (FR-053): style is BJCP-valid but not assigned to the resolved category under this competition's FR-052 configuration. Malformed/empty file → `400 invalid-import-file`. A prior unconsolidated batch for the competition is discarded (single active batch). |
| `GET /competitions/{id}/imports/{importId}` | ORGANIZER | Current row states — `status` may additionally be `Excluded` for rows resolved via the exclude action (see below). |
| `POST /competitions/{id}/imports/{importId}/revalidate` | ORGANIZER | FR-054: re-runs category/style/allow-list resolution for every row that isn't `Invalid`/`Excluded`, against the competition's *current* categories and style assignments — lets the organizer fix categories/styles in the wizard's step 3 and pick back up without re-uploading. Same response shape as `GET`. No-op once the batch is `Consolidated`. |
| `PUT /competitions/{id}/imports/{importId}/rows/{rowNumber}` | ORGANIZER | Full-replace edit of the row's `data` shape (see above) — `competitionCategoryId` (if given) must belong to this competition, `styleCode` (if given) must exist in the BJCP 2021 catalog, either failing → `400 invalid-import-file`. `status` recomputes to `Valid` once a well-formed `participantEmail`, `competitionCategoryId`, and `styleCode` are all present *and* the style is assigned to that category (FR-053), `CategoryStyleMismatch`/`Invalid` otherwise. Rejected with `400 invalid-import-file` on an already-`Excluded` row (terminal). |
| `POST /competitions/{id}/imports/{importId}/rows/{rowNumber}/exclude` | ORGANIZER | One-way: sets `status: "Excluded"`, terminal for the row (no longer editable). |
| `POST /competitions/{id}/imports/{importId}/consolidate` | ORGANIZER | `409 unresolved-import-rows` while any row is `StyleMismatch`/`CategoryMismatch`/`CategoryStyleMismatch`/`Invalid` (FR-011). `409 invalid-state-transition` if the batch was already consolidated (no re-consolidation). On success `200` `{ imported, excluded, entries: [{ id, blindCode, styleCode }] }` — blind codes generated here (FR-013); `excluded` counts `Excluded` rows. A row's `participantEmail` matching an existing Participant within this same competition updates that Participant's fields (last-import-wins) instead of creating a duplicate. |

## Entries (organizer)

| Method & Path | Role | Description |
|---|---|---|
| `GET /competitions/{id}/entries` | ORGANIZER | Every entry in the competition with its current table assignment — feeds the table-setup UI's "Unassigned" column (T048). `200` → `[{ id, blindCode, styleCode, styleName, abvLow, abvHigh, beerName, notValidForBos, tastingTableId, tastingTableName }]`; `tastingTableId`/`tastingTableName` are `null` until the entry is assigned via `POST`/`PUT .../tables`. Organizer-only, so unlike judge-facing DTOs this includes `beerName` (no BR-01 concern). |

## Judges

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions/{id}/judges` | ORGANIZER | Body: `{ emails: [string] }`. Creates missing profiles and enqueues `ProvisionJudgeAccount` per new judge (Keycloak account created in the background, no email sent — Session 2026-08-02/R-20, supersedes the originally-automatic `SendInvitation` enqueue). → `201` `{ created: [{ id, email }], skipped: [{ email, reason: "duplicate-in-list" \| "already-registered" }] }` (FR-014/FR-015). |
| `GET /competitions/{id}/judges` | ORGANIZER | `[{ id, email, displayName, bjcpRank, bjcpId, preferredCategory, preferences, invitationStatus, attempts, lastError, sentAt }]`. The four roster fields are `null` for judges created via the plain email-list flow above. |
| `PUT /competitions/{id}/judges/{judgeId}` | ORGANIZER | Correct a judge's email before first login (edge case: bounced invitation). Body: `{ email }`. Re-validates uniqueness (FR-015), updates the Keycloak account. `409 judge-already-active` once the judge has authenticated. **COI matching / BOS re-flagging against the new address (FR-017/FR-018) is still not implemented here** — `Features/Tables` now exists (Phase 7), so the blocking dependency is resolved, but wiring this endpoint to `CoiDetector`/`BosFlagRules` was never in either phase's task scope; tracked as an explicit follow-up (see `Docs/arquitectura_viva.md` Recorded debt). |
| `POST /competitions/{id}/judges/{judgeId}/invitation` | ORGANIZER | Re-send invitation to this one judge (edge case: bounced email after correction). Enqueues `SendInvitation` regardless of current `invitationStatus`. |
| `POST /competitions/{id}/judges/notify` | ORGANIZER | *(added FR-059)* Enqueues `SendInvitation` for every judge in the competition whose `invitationStatus` is `Pending` — the explicit "Notify judges" action, decoupled from both provisioning paths above. → `200` `{ queued: [{ id, email }] }`. A judge already `Sent`/`Failed` is untouched by this bulk action (use the per-judge resend above for those). No-op (`queued: []`) when nothing is `Pending`. |

## Judge Roster Import

*(added US14 — FR-055–FR-058. Mirrors Entry Import below 1:1; same upload → correction → consolidate*
*shape, `contracts/judge-import-file.md` for the file format. On consolidation, see FR-057/R-20 —*
*profiles persist and `ProvisionJudgeAccount` is enqueued per judge; no invitation is sent here.)*

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions/{id}/judge-imports` | ORGANIZER | Multipart `.xlsx` upload (schema: [judge-import-file.md](./judge-import-file.md)). Competition must be `Draft`/`Active` (`409 invalid-state-transition` otherwise, same gate as `POST .../imports`). → `201` `{ importId, rows: [{ rowNumber, status: "Valid" \| "Invalid", data, error? }] }` where `data` is `{ name, email, bjcpRank, bjcpId, preferredCategory, preferences }`. Malformed/empty file → `400 invalid-import-file`. A prior unconsolidated judge-roster batch for the competition is discarded (single active batch, same rule as beer-entry import). |
| `GET /competitions/{id}/judge-imports/{importId}` | ORGANIZER | Current row states — `status` may additionally be `Excluded` for rows resolved via the exclude action below. |
| `PUT /competitions/{id}/judge-imports/{importId}/rows/{rowNumber}` | ORGANIZER | Full-replace edit of the row's `data` shape (see above). `status` recomputes to `Valid` once `name` and `email` are both present and well-formed, `Invalid` otherwise. Rejected with `400 invalid-import-file` on an already-`Excluded` row (terminal). |
| `POST /competitions/{id}/judge-imports/{importId}/rows/{rowNumber}/exclude` | ORGANIZER | One-way: sets `status: "Excluded"`, terminal for the row. |
| `POST /competitions/{id}/judge-imports/{importId}/consolidate` | ORGANIZER | `409 unresolved-import-rows` while any row is `Invalid` (FR-056). `409 invalid-state-transition` if the batch was already consolidated. On success `200` `{ created: [{ id, email }], updated: [{ id, email }], excluded: number, skipped: [{ email, reason }] }` — `updated` covers a `Valid` row whose email matches an existing judge in this competition (last-import-wins on the roster fields, same policy as beer-entry `Participant` matching); `created`/`updated` judges each get a `ProvisionJudgeAccount` job enqueued (FR-057), no email sent — except an `updated` judge whose invitation has already left `Pending` (already `Sent`/`Failed`), which is a no-op (re-provisioning would reset a password that may already have been delivered; see R-20). `skipped` reports rows dropped as duplicate emails within the same file (FR-058, same policy/shape as `POST /competitions/{id}/judges` `skipped` — `reason: "duplicate-in-list"`); the later row's values still win on the resulting judge (last-import-wins), only earlier same-email rows are reported as skipped. |

## Tables (organizer)

| Method & Path | Role | Description |
|---|---|---|
| `POST /competitions/{id}/tables` | ORGANIZER | Body: `{ name, judgeIds: [], beerEntryIds: [] }`. Competition must be `Draft`/`Active` (`409 invalid-state-transition` otherwise, same gate as `POST .../imports` — data-model.md §Competition state gates). COI violation → `409 conflict-of-interest` `{ conflicts: [{ judgeId, beerEntryIds }] }`, nothing persisted (FR-017). Success → `201`; response includes `bosFlaggedEntryIds` when FR-018 fired. |
| `PUT /competitions/{id}/tables/{tableId}` | ORGANIZER | Same body, COI semantics, and `Draft`/`Active` state gate as `POST`. `409 table-closed` if the table itself is closed. |
| `GET /competitions/{id}/tables` | ORGANIZER | Tables with judges, samples, progress, state. |
| `DELETE /competitions/{id}/tables/{tableId}/judges/{judgeId}` | ORGANIZER | Live removal (FR-039 — "during the live event"). Competition must be `InEvaluation` (`409 invalid-state-transition` otherwise). `200`; sets `RemovedAt`, revokes access, emits `JudgeRemoved`, audit-logged. Already-submitted evaluations stay valid. |

## Judge workspace

All endpoints below return `404` for tables/samples the caller is not assigned to. Payloads are
built exclusively from the blind projection (`JudgeSampleDto`) — see data-model.md §Anonymity.

| Method & Path | Role | Description |
|---|---|---|
| `GET /me/tables` | JUDGE | Assigned tables: `[{ tableId, name, competitionState, tableState, orderFixed, orderFixedBy? }]`. Competition must be `Active`+ (invisible in `Draft`). |
| `GET /me/tables/{tableId}/samples` | JUDGE | `[JudgeSampleDto]` = `{ beerEntryId, blindCode, styleCode, styleName, sequenceOrder?, evaluationStatus: "NotStarted" \| "Submitted" \| "PendingConsensus", entryInstructions? }` — `entryInstructions` is the one deliberate exception to the BR-01/FR-019 anonymity boundary alongside `blindCode`/`styleCode`. |
| `POST /me/tables/{tableId}/order` | JUDGE | Body: `{ orderedBeerEntryIds: [] }` (must be a permutation of the table's samples → `400`). One-shot: `409 order-already-fixed` `{ fixedBy }` if raced (US6-4). Requires competition `Active`/`InEvaluation`. Emits `TableOrderFixed`. |
| `POST /me/tables/{tableId}/evaluations` | JUDGE | Header `X-Idempotency-Key: {competitionId}:{tableId}:{judgeId}:{entryId}`. Body: `{ beerEntryId, scores: { aroma, appearance, flavor, mouthfeel, overall }, comments: { aroma, appearance, flavor, mouthfeel, overall } }`. Validation (FR-023/FR-025): caps 12/3/20/5/10, comments ≥ 20 chars → `400`. Preconditions: competition `InEvaluation` (`409 invalid-state-transition`), order fixed (`409 order-not-fixed`), sample is next in sequence (`409 out-of-sequence`), table open (`409 table-closed`). Success → `201` `{ evaluationId, status: "Confirmed" \| "PendingConsensus", total, discrepancy? }`. Idempotent replay → `200` with the stored result (R-07). |
| `PUT /me/tables/{tableId}/evaluations/{evaluationId}` | JUDGE | Allowed **only** while an open discrepancy covers it and caller is involved → else `409 evaluation-locked` (Clarification Q2). Re-runs discrepancy check; may resolve the alert. |
| `GET /me/tables/{tableId}/discrepancies` | JUDGE | Open alerts involving the caller: `[{ alertId, blindCode, totals: [{ judgeDisplayName, total, isMine, evaluationId }] }]`. `evaluationId` addresses the `PUT` above. |
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
| `invalid-import-file` | 400 | unreadable/empty/mis-schema’d upload (beer-entry or judge-roster, `POST .../imports` or `.../judge-imports`) |
| `invalid-state-transition` | 409 | FR-006 gates (skip/reverse/edit in wrong state) |
| `conflict-of-interest` | 409 | FR-017 table assignment |
| `unresolved-import-rows` | 409 | FR-011 consolidation (beer-entry); FR-056 consolidation (judge-roster) |
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
