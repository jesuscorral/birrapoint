# Data Model: BirraPoint MVP

**Date**: 2026-07-06 | **Plan**: [plan.md](./plan.md) | **Spec**: [spec.md](./spec.md)

Server of record: PostgreSQL via EF Core code-first. Client-side stores (Dexie) at the end.
All PKs are `Guid` (v7/sequential). All entities carry `CreatedAt`/`UpdatedAt` (UTC).

## Entity Catalog

### Competition

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| Name | string(200) | required |
| Venue | string(200) | required |
| StartDate | DateOnly | required |
| EndDate | DateOnly | required, `>= StartDate` (DB check constraint) |
| Description | string(2000)? | optional |
| LogoUrl | string(500)? | optional |
| EntryLimit | int? | optional, > 0 |
| StartRegistration | DateOnly? | optional |
| EndRegistration | DateOnly? | optional; `>= StartRegistration` when both set (DB check constraint) |
| State | enum `CompetitionState` | `Draft` \| `Active` \| `InEvaluation` \| `Finalized` |
| CreatedByUserId | string | Keycloak subject of the organizer; remains the source of truth for every existing ownership check (`c.CreatedByUserId == currentUser.Sub`) |
| OrganizerId | Guid? | FK → Organizer; additive, populated going forward by `CreateCompetitionCommandHandler` only |

### Organizer

Account-level profile for a competition organizer; identity lives in Keycloak (Principle VII). One
Organizer owns many Competition rows (1─\*). Resolved-or-created lazily on first authenticated
action via `IOrganizerResolver` — unlike Judge, no row can pre-exist before that first login, since
organizers self-register (`registrationAllowed`) rather than being invited ahead of time.

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| KeycloakUserId | string(255) | required; unique; Keycloak subject (JWT `sub`) |
| Email | string(320) | required; unique |
| FirstName | string(200) | required; from JWT `given_name`, falls back to `"Organizer"` |
| LastName | string(200) | required; from JWT `family_name`, falls back to the Keycloak subject |

**State machine (FR-006, forward-only, organizer-only):**

```text
Draft ──► Active ──► InEvaluation ──► Finalized
```

| State | Gates |
|-------|-------|
| Draft | Organizer setup only (wizard, import, judges, tables); invisible to judges |
| Active | Judges see assignments; order can be fixed; sheets locked; setup still editable |
| InEvaluation | Sheets unlock (subject to fixed order); imports & wizard edits rejected (409) |
| Finalized | Everything read-only; dispatch pipeline runs. Requires all tables `Closed` |

### BjcpStyle *(read-only seed, FR-012/FR-049)*

| Field | Type | Constraints |
|-------|------|-------------|
| Code | string(20) | PK, e.g. `21A`; widened from string(5) — categories with no official BJCP letter subcode (Historical Beer, Appendix B local styles, named Specialty-IPA variants) use slug-style codes up to 17 chars (e.g. `27-KentuckyCommon`) |
| Name | string(100) | required, e.g. `American IPA` |
| CategoryNumber | string(3) | required, e.g. `21` |
| CategoryName | string(100) | required, e.g. `IPA` |
| OGLow / OGHigh | decimal(4,3)? | optional — some Specialty styles have no fixed range |
| FGLow / FGHigh | decimal(4,3)? | optional |
| IBULow / IBUHigh | int? | optional |
| SRMLow / SRMHigh | decimal(5,1)? | optional |
| ABVLow / ABVHigh | decimal(4,1)? | optional |
| DescriptionJson | jsonb | required; BJCP 2021 guide text — see shape below (FR-049) |

`DescriptionJson` shape (all string fields hold the Spanish guide text verbatim; `entryInstructions`
is null for styles without an "Instrucciones para la inscripción" section):

```json
{
  "overallImpression": "string",
  "aroma": "string",
  "appearance": "string",
  "flavor": "string",
  "mouthfeel": "string",
  "comments": "string",
  "history": "string",
  "characteristicIngredients": "string",
  "styleComparison": "string",
  "entryInstructions": "string | null",
  "commercialExamples": ["string", "..."],
  "tags": ["string", "..."]
}
```

Sourced from the official BJCP 2021 Style Guidelines (Spanish translation); seeded from
`Features/Catalog/Data/bjcp-2021.json` (T010). Covers categories 1–34 plus the BJCP Appendix B
local styles (X1–X5, e.g. `Italian Grape Ale`, `Catharina Sour`, `New Zealand Pilsner`).

### CompetitionCategory *(organizer-defined, FR-052, wizard step 3)*

Free-text grouping, chosen per competition by the organizer (e.g. "Estilos clásicos"), used to
select which BJCP styles are allowed for that competition and how they're organized. **Not** the
same concept as `BjcpStyle.CategoryName`/`CategoryNumber` (the BJCP taxonomy's own official
category, e.g. "21"/"IPA") — that field is read-only catalog metadata; this entity is an
organizer-editable allow-list container. One Competition has many CompetitionCategory rows (1─\*).

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition, `Cascade` delete |
| Name | string(100) | required; **unique (CompetitionId, Name)** |
| DisplayOrder | int | organizer-chosen ordering |

### CompetitionCategoryStyle *(join, FR-052)*

Assigns one BjcpStyle to one CompetitionCategory. A style can be assigned to **at most one**
category per competition (DB-enforced) — not every BJCP style needs to be assigned; an unassigned
style is simply not part of this competition's allow-list. `CompetitionId` is a denormalized copy
of the owning category's competition id, needed only so the uniqueness constraint below doesn't
require a cross-table subquery.

| Field | Type | Constraints |
|-------|------|-------------|
| CompetitionCategoryId | Guid | PK (composite), FK → CompetitionCategory, `Cascade` delete |
| StyleCode | string(20) | PK (composite), FK → BjcpStyle, `Restrict` delete (catalog is read-only) |
| CompetitionId | Guid | denormalized FK → Competition, `NoAction` delete (the CompetitionCategory cascade already handles cleanup — two independent cascade paths to Competitions from this table would conflict); **unique (CompetitionId, StyleCode)** enforces "at most one category per style per competition" |

Import validates the "Categoria" cell against `CompetitionCategory.Name` (import-file.md) and sets
`BeerEntry.CompetitionCategoryId` at consolidation. It also cross-checks that the row's resolved
style is one of that category's assigned styles via this join table (FR-053): a style that is
BJCP-valid but not assigned to the resolved category yields `ImportRowStatus.CategoryStyleMismatch`
instead of `Valid`. This check runs at initial parse, at the full-row edit, and at revalidation
(see `ImportRow.Status` below).

### Participant *(brewer — never exposed to judges)*

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition |
| Name | string(200) | required |
| Email | string(320) | required; **unique (CompetitionId, Email)** |
| AcceMemberNumber | string(50)? | ACCE club membership number (import-file.md); stored as text |
| DateOfBirth | DateOnly? | |
| Phone | string(30)? | |

### BeerEntry *(sample)*

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition |
| ParticipantId | Guid | FK → Participant |
| BeerName | string(200)? | optional — the ACCE import format has no beer-name column; always null coming out of import, organizer-editable afterward. **Never serialized into judge-facing DTOs** |
| StyleCode | string(20) | FK → BjcpStyle |
| BlindCode | string(10) | system-generated at consolidation; **unique (CompetitionId, BlindCode)** |
| CompetitionCategoryId | Guid? | FK → CompetitionCategory, `Restrict` delete (a category referenced by entries can't be deleted out from under them). Nullable only because entries seeded outside the Import slice predate this field; the Import consolidation flow always sets it |
| SubmittedAt | DateTimeOffset | "Marca temporal" — when the organizer's original entry form was submitted |
| AbvPercent | decimal(4,2) | "Grado alcohol: (%)" |
| BrewDate | DateOnly? | "Fecha de elaboración" |
| BottlingDate | DateOnly? | "Fecha de embotellado" |
| Malts | string(1000)? | "Maltas utilizadas" — stored verbatim |
| Hops | string(1000)? | "Lupulos utilizados" — stored verbatim |
| Yeast | string(1000)? | "Levadura utilizada" — stored verbatim |
| OtherIngredients | string(1000)? | "Otros ingredientes" — stored verbatim |
| EntryInstructions | string(1000)? | "Instrucciones de entrada" — **judge-facing**, the one deliberate exception to BR-01/FR-019 alongside BlindCode/StyleCode |
| NotValidForBos | bool | default false; set per FR-018 |

### EntryCollaborator

| Field | Type | Constraints |
|-------|------|-------------|
| BeerEntryId | Guid | composite PK, FK → BeerEntry |
| Email | string(320) | composite PK; used for COI matching (FR-017) |

Not populated by the ACCE import format (no collaborators column) — used elsewhere when
collaborators are assigned by some other means, out of Import's scope.

### ImportBatch *(slice-owned staging area, Features/Import)*

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition |
| Status | enum `ImportBatchStatus` | `Pending` \| `Consolidated`; **at most one `Pending` batch per competition** (partial unique index on `Status = 'Pending'`) — a new upload discards the prior unconsolidated batch (import-file.md §Semantics) |

### ImportRow *(slice-owned staging area, Features/Import)*

Mirrors the ACCE `.xlsx` column set 1:1 (import-file.md) so the Mapping & Correction screen
operates purely off staged data before consolidation.

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| ImportBatchId | Guid | FK → ImportBatch; **unique (ImportBatchId, RowNumber)** |
| RowNumber | int | 1-based position among the file's data rows (excludes the header row) |
| Status | enum `ImportRowStatus` | `Valid` \| `StyleMismatch` \| `CategoryMismatch` \| `CategoryStyleMismatch` \| `Invalid` \| `Excluded` — the first five are parse-time outcomes (import-file.md; `CategoryStyleMismatch` per FR-053 — category and style each individually valid, but the style isn't assigned to that category); `Excluded` is a resolution outcome set only via `POST .../rows/{rowNumber}/exclude`. `Valid` and `Excluded` never block consolidation; the other four do (FR-011) |
| ParticipantName | string(200)? | raw parsed cell, may be null/malformed when Status = `Invalid` |
| ParticipantEmail | string(320)? | raw parsed cell |
| AcceMemberNumberText | string(50)? | raw parsed cell, stored as plain digits when the source cell was numeric |
| DateOfBirth | DateOnly? | raw parsed cell |
| Phone | string(30)? | raw parsed cell, stored as plain digits when the source cell was numeric |
| CategoryText | string(200)? | raw "Categoria" cell text — may not match any of this competition's CompetitionCategory names |
| ResolvedCompetitionCategoryId | Guid? | FK → CompetitionCategory (not DB-enforced, staging data); set at parse time when CategoryText matched, or by the organizer via the full row edit |
| StyleText | string(200)? | raw "Estilo" cell text as read from the file — may not match any catalog style |
| ResolvedStyleCode | string(20)? | FK → BjcpStyle.Code (not DB-enforced, staging data); set at parse time when Estilo matched, or by the organizer via the full row edit |
| SubmittedAt | DateTimeOffset? | raw parsed "Marca temporal" cell |
| AbvPercent | decimal(4,2)? | raw parsed "Grado alcohol: (%)" cell |
| BrewDate | DateOnly? | raw parsed cell |
| BottlingDate | DateOnly? | raw parsed cell |
| Malts, Hops, Yeast, OtherIngredients, EntryInstructions | string(1000)? each | raw parsed cells |
| BeerName | string(200)? | always null coming out of import; purely organizer-editable via the full row edit |
| ErrorMessage | string(1000)? | present for `StyleMismatch`/`CategoryMismatch`/`Invalid`; null once resolved |

These two entities are staging data only — never referenced outside the Import slice. On
consolidation (FR-013), every `Valid` row becomes a `Participant` (matched by email within the
competition — an existing row's Name/AcceMemberNumber/DateOfBirth/Phone are updated from the row,
last-import-wins; otherwise a new row is created) + `BeerEntry` (with a generated unique
`BlindCode` and `CompetitionCategoryId` set from `ResolvedCompetitionCategoryId`); `Excluded` rows
are simply skipped.

**Revalidation** (FR-054, `POST .../imports/{importId}/revalidate`) re-runs category/style/allow-
list resolution for every row currently `Valid`/`StyleMismatch`/`CategoryMismatch`/
`CategoryStyleMismatch` (a `CompetitionCategory` full-replace PUT — `SetCompetitionCategories.cs`
— deletes and recreates rows, so a previously-resolved `CompetitionCategoryId` may now point at a
deleted id even when a same-named category still exists). Per row: if the currently-resolved
category id still exists it is kept, otherwise it is re-matched by the row's raw `CategoryText`;
same for style by `ResolvedStyleCode`/`StyleText`; the two are then cross-checked against the
current `CompetitionCategoryStyle` allow-list. `Invalid` and `Excluded` rows are skipped (their
failure/exclusion is unrelated to category/style state). No-op (returns the batch unchanged) once
`ImportBatch.Status` is `Consolidated`.

### JudgeImportBatch *(slice-owned staging area, Features/Judges — added US14/FR-055)*

Mirrors `ImportBatch` exactly, same semantics (`contracts/judge-import-file.md` §Semantics): at
most one `Pending` batch per competition, a new upload discards the prior unconsolidated one.

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition |
| Status | enum `JudgeImportBatchStatus` | `Pending` \| `Consolidated`; **at most one `Pending` batch per competition** (partial unique index on `Status = 'Pending'`) |

### JudgeImportRow *(slice-owned staging area, Features/Judges — added US14/FR-055)*

Mirrors the judge-roster `.xlsx` column set 1:1 (`contracts/judge-import-file.md`). Simpler than
`ImportRow`: no catalog resolution happens for any judge-roster field, so there is no
mismatch/unresolved-reference status — only whether the two required fields are present.

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| JudgeImportBatchId | Guid | FK → JudgeImportBatch; **unique (JudgeImportBatchId, RowNumber)** |
| RowNumber | int | 1-based position among the file's data rows (excludes the header row) |
| Status | enum `JudgeImportRowStatus` | `Valid` \| `Invalid` \| `Excluded` — `Invalid` when `Name` or `Email` is missing/malformed (FR-056); `Valid` and `Excluded` never block consolidation, `Invalid` does (FR-056) |
| Name | string(200)? | raw parsed "Nombre y apellidos" cell, may be null/malformed when Status = `Invalid` |
| Email | string(320)? | raw parsed "Correo electrónico" cell |
| BjcpRank | string(100)? | raw parsed "Rango BJCP" cell |
| BjcpId | string(50)? | raw parsed "BJCP ID" cell, stored verbatim (incl. the `Pte` placeholder) |
| PreferredCategory | string(200)? | raw parsed "Categoría preferida" cell |
| Preferences | string(2000)? | raw parsed "Preferencias" cell, stored verbatim as plain text (never interpreted as markup — R-20) |
| ErrorMessage | string(1000)? | present for `Invalid`; null once resolved |

On consolidation (FR-057), every `Valid` row upserts a `Judge` (matched by email within the
competition — an existing row's `BjcpRank`/`BjcpId`/`PreferredCategory`/`Preferences` are updated
from the row, last-import-wins, same policy as `Participant` in beer-entry consolidation) +
`Invitation{Status=Pending}` if one doesn't already exist, and enqueues a `ProvisionJudgeAccount`
job (R-20); `Excluded` rows are skipped. Duplicate emails within the same file resolve to a single
upsert, reported the same way `ImportBatch` consolidation reports beer-entry duplicates (FR-058).

### Judge *(competition-scoped judge profile)*

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition; **unique (CompetitionId, Email)** |
| Email | string(320) | required; COI matching key vs Participant.Email + EntryCollaborator.Email |
| KeycloakUserId | string? | set once provisioned in Keycloak |
| DisplayName | string(200) | defaults to email local-part until first login |
| BjcpRank | string(100)? | *(added US14/FR-057)* free text, club vocabulary (e.g. "Certificado", "Reconocido", "Pendiente de Rango") — not a controlled catalog |
| BjcpId | string(50)? | *(added US14/FR-057)* stored verbatim; observed formats include `E####`, bare numeric, and the placeholder `Pte` ("not yet assigned") — never parsed or validated |
| PreferredCategory | string(200)? | *(added US14/FR-057)* free text; informational only, not cross-checked against this competition's `CompetitionCategory` names (spec.md Assumptions) |
| Preferences | string(2000)? | *(added US14/FR-057)* free-text notes (table-mate requests, availability, aversions); rendered as plain text only — never interpreted as markup, even when the source cell contains literal `<br>`-style text |

Judges created via either provisioning path — the email-list flow (FR-014) or the roster import
(FR-057) — are the same entity; `BjcpRank`/`BjcpId`/`PreferredCategory`/`Preferences` are simply
null for judges created via the simpler email-list flow, which doesn't collect them.

### Invitation

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| JudgeId | Guid | FK → Judge |
| Status | enum | `Pending` \| `Sent` \| `Failed` |
| Attempts | int | default 0 |
| LastError | string? | last SMTP failure |
| SentAt | DateTimeOffset? | |

**Behavior change (Session 2026-08-02, R-20)**: `Pending` no longer auto-progresses to `Sent`
shortly after `Judge` creation. Both provisioning paths (FR-014, FR-057) create the `Judge` +
`Invitation{Status=Pending}` row and enqueue a `ProvisionJudgeAccount` job (creates the Keycloak
account only, no email); `Status` stays `Pending` until the organizer's explicit "Notify judges"
action (FR-059) enqueues `SendInvitation` for every `Pending` judge in the competition, which sends
the email and transitions to `Sent`/`Failed` exactly as it does today. The existing per-judge
resend (`POST .../judges/{judgeId}/invitation`) is unchanged and still applies after a `Failed` or
already-`Sent` invitation.

### TastingTable

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition |
| Name | string(100) | required; unique (CompetitionId, Name) |
| State | enum `TableState` | `Open` \| `Closed` |
| OrderFixedByJudgeId | Guid? | FK → Judge; null until order fixed |
| OrderFixedAt | DateTimeOffset? | null until order fixed |
| ClosedAt | DateTimeOffset? | |

**Invariants:** order fix is one-shot (second attempt → 409, Clarification Q1 / US6-4);
`Closed` requires: every (judge × sample) evaluation submitted **and** zero open
`DiscrepancyAlert` (FR-032/FR-033); `Closed` is terminal.

### TableJudge

| Field | Type | Constraints |
|-------|------|-------------|
| TastingTableId | Guid | composite PK |
| JudgeId | Guid | composite PK |
| RemovedAt | DateTimeOffset? | set on live removal (FR-039); rows are never hard-deleted once evaluations exist |

**COI invariant (FR-017):** insert rejected if the judge's email matches the owner or any
collaborator of any `TableSample` entry at this table — checked transactionally at assignment.

### TableSample

| Field | Type | Constraints |
|-------|------|-------------|
| TastingTableId | Guid | composite PK |
| BeerEntryId | Guid | composite PK; an entry belongs to at most one table: **unique (BeerEntryId)** |
| SequenceOrder | int? | null until order fixed; then 1..M, unique (TastingTableId, SequenceOrder) |

### Evaluation

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| TastingTableId | Guid | FK |
| JudgeId | Guid | FK |
| BeerEntryId | Guid | FK; **unique (JudgeId, BeerEntryId)** — idempotency backstop (FR-029, R-07) |
| AromaScore | int | 0–12 |
| AppearanceScore | int | 0–3 |
| FlavorScore | int | 0–20 |
| MouthfeelScore | int | 0–5 |
| OverallScore | int | 0–10 |
| AromaComment … OverallComment | string(2000) ×5 | each required, min length 20 (FR-025) |
| Total | int | **computed column** = sum of the five scores (never client-supplied, FR-024) |
| Status | enum `EvaluationStatus` | `Confirmed` \| `PendingConsensus` |
| SubmittedAt | DateTimeOffset | required |

**Lifecycle (Clarifications Q2 + FR-030/FR-031/FR-034):**

```text
(client draft — never on server)
        │ submit
        ▼
 PendingConsensus ◄──► adjusted via open DiscrepancyAlert only
        │ all totals within 7
        ▼
    Confirmed ──(table close)──► read-only for judges (state unchanged; lock derived
                                  from TastingTable.State = Closed)
```

Terminology: the spec's "held as provisional" state is `PendingConsensus` here and in the API
contract. Submission with no >7 divergence goes directly to `Confirmed`. Judge mutation rules:
- `INSERT`: only own evaluation, own open table, competition `InEvaluation`, sample is the next
  in sequence (FR-022).
- `UPDATE`: only while an open `DiscrepancyAlert` covers this evaluation's (table, entry) and the
  judge is involved.
- Anything else → 409 ProblemDetails.

### DiscrepancyAlert

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| TastingTableId | Guid | FK |
| BeerEntryId | Guid | FK; **unique open alert per (TastingTableId, BeerEntryId)** (partial unique index on Status = Open) |
| Status | enum | `Open` \| `Resolved` |
| CreatedAt / ResolvedAt | DateTimeOffset / ? | |

Involved judges are derived at read time: every judge whose total for the entry differs by > 7
points from any other submitted total (spec edge case: ≥3 judges).

### DispatchJob *(R-06 background queue)*

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK |
| Type | enum | `GeneratePdfs` \| `BundleZip` \| `SendResultEmail` \| `SendInvitation` \| `ProvisionJudgeAccount` *(added R-20)* |
| PayloadJson | jsonb | e.g. `{ "participantId": … }` |
| Status | enum | `Pending` \| `Running` \| `Completed` \| `Failed` |
| Attempts | int | retry with backoff; `Failed` after max attempts, retryable via API (FR-041) |
| LastError | string? | truncated to 2000 chars |
| NextAttemptAt | DateTimeOffset? | null until a failed attempt schedules a backoff-delayed retry (ADR-0008); a `Pending` job is dispatch-eligible only once this has passed or is null; indexed together with `Status` |

### GeneratedScoreSheet / ResultsArchive *(T074/T075, US10 — added during implementation)*

Neither entity was in the original design pass; added when implementing US10 revealed no blob/file
storage was ever decided anywhere in this stack (`plan.md`'s Storage section is Postgres +
IndexedDB only). PDF and ZIP bytes are stored directly in Postgres (`bytea`) — the only durable
medium this stack has, consistent with Postgres being the server of record.

| Field | Type | Constraints |
|-------|------|-------------|
| **GeneratedScoreSheet** | | one row per `BeerEntry`, upserted on regeneration |
| Id | Guid | PK |
| BeerEntryId | Guid | FK; unique (upsert key) |
| PdfBytes | bytea | rendered by `ScoreSheetDocument` (QuestPDF) |
| GeneratedAt | DateTimeOffset | |
| **ResultsArchive** | | one row per `Competition`, upserted on regeneration |
| Id | Guid | PK |
| CompetitionId | Guid | FK; unique (upsert key) |
| ZipBytes | bytea | `/CompetitionName/ParticipantID/Style_BlindCode.pdf` hierarchy (FR-040) |
| GeneratedAt | DateTimeOffset | |

### AuditLog

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| ActorUserId | string | Keycloak subject |
| Action | string(100) | e.g. `EvaluationCorrected`, `JudgeRemoved`, `TableClosed`, `StateChanged` |
| EntityType / EntityId | string(100) / string(50) | target reference |
| DataJson | jsonb | before/after payload (FR-035, FR-039) — MUST NOT log evaluation comment bodies beyond diffs; never logs credentials |
| OccurredAt | DateTimeOffset | |

## Anonymity boundary (BR-01 / FR-019 — structural enforcement)

Judge-facing read models are built exclusively from this projection and MUST live in a dedicated
DTO namespace (`Features/*/JudgeDtos`) that has no properties for entrant data:

```text
JudgeSampleDto { BeerEntryId, BlindCode, StyleCode, StyleName, SequenceOrder?, EvaluationStatus, EntryInstructions? }
```

`BeerName`, `Participant.*`, `EntryCollaborator.*` are never referenced by any judge-facing
query. `EntryInstructions` is the one deliberate exception to this boundary (import-file.md) —
free text the organizer enters per entry, alongside BlindCode/StyleCode. Contract tests assert the
serialized payloads contain none of the entrant fields, while still surfacing EntryInstructions.

## Client-side stores (Dexie / IndexedDB — R-08)

| Store | Key | Contents |
|-------|-----|----------|
| `drafts` | `beerEntryId` | in-progress sheet fields; written ≤300 ms after each change (SC-003); deleted on successful submit |
| `outbox` | `idempotencyKey` | submitted-but-unsynced evaluation payloads + attempt metadata; replayed per R-08. Secondary index on `tastingTableId` (T020) — T087 looks up "outbox items for this table" when a judge is removed |

Client stores are caches, never sources of truth; a device wipe loses only unsynced work, which
the UI surfaces via the offline badge (FR-027).

## Relationship overview

```text
Organizer 1─* Competition (OrganizerId, additive alongside the existing CreatedByUserId claim check)
Competition 1─* Participant 1─* BeerEntry *─1 BjcpStyle
Competition 1─* CompetitionCategory 1─* CompetitionCategoryStyle *─1 BjcpStyle (style in ≤1 category per competition)
Competition 1─* ImportBatch 1─* ImportRow (staging; consolidates into Participant/BeerEntry/EntryCollaborator)
Competition 1─* JudgeImportBatch 1─* JudgeImportRow (staging; consolidates into Judge — added US14/FR-055)
Competition 1─* Judge 1─* Invitation
Competition 1─* TastingTable 1─* TableJudge *─1 Judge
TastingTable 1─* TableSample *─1 BeerEntry (entry in at most one table)
TastingTable 1─* Evaluation *─1 Judge ; Evaluation *─1 BeerEntry
TastingTable 1─* DiscrepancyAlert *─1 BeerEntry
Competition 1─* DispatchJob
Competition 1─1 ResultsArchive
BeerEntry 1─1 GeneratedScoreSheet
BeerEntry 1─* EntryCollaborator
```
