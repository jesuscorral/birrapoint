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
| CreatedByUserId | string | Keycloak subject of the organizer |

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

### Participant *(brewer — never exposed to judges)*

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition |
| Name | string(200) | required |
| Email | string(320) | required; **unique (CompetitionId, Email)** |

### BeerEntry *(sample)*

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition |
| ParticipantId | Guid | FK → Participant |
| BeerName | string(200) | required — **never serialized into judge-facing DTOs** |
| StyleCode | string(20) | FK → BjcpStyle |
| BlindCode | string(10) | system-generated at consolidation; **unique (CompetitionId, BlindCode)** |
| NotValidForBos | bool | default false; set per FR-018 |

### EntryCollaborator

| Field | Type | Constraints |
|-------|------|-------------|
| BeerEntryId | Guid | composite PK, FK → BeerEntry |
| Email | string(320) | composite PK; used for COI matching (FR-017) |

### ImportBatch *(slice-owned staging area, Features/Import)*

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition |
| Status | enum `ImportBatchStatus` | `Pending` \| `Consolidated`; **at most one `Pending` batch per competition** (partial unique index on `Status = 'Pending'`) — a new upload discards the prior unconsolidated batch (import-file.md §Semantics) |

### ImportRow *(slice-owned staging area, Features/Import)*

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| ImportBatchId | Guid | FK → ImportBatch; **unique (ImportBatchId, RowNumber)** |
| RowNumber | int | 1-based position among the file's data rows (excludes the header row) |
| Status | enum `ImportRowStatus` | `Valid` \| `StyleMismatch` \| `Invalid` \| `Excluded` — the first three are parse-time outcomes (import-file.md); `Excluded` is a resolution outcome set only via the `exclude` action. `Valid` and `Excluded` never block consolidation; `StyleMismatch`/`Invalid` do (FR-011) |
| ParticipantName | string(200)? | raw parsed cell, may be null/malformed when Status = `Invalid` |
| ParticipantEmail | string(320)? | raw parsed cell |
| BeerName | string(200)? | raw parsed cell |
| StyleText | string(200)? | raw cell text as read from the file — may not match any catalog style |
| CollaboratorsJson | jsonb | JSON array of the semicolon-split, trimmed collaborator emails |
| ResolvedStyleCode | string(20)? | FK → BjcpStyle.Code (not DB-enforced, staging data); set at parse time when Style matched, or by the organizer via the `assign-style` resolution action |
| ErrorMessage | string(1000)? | present for `StyleMismatch`/`Invalid`; null once resolved |

These two entities are staging data only — never referenced outside the Import slice. On
consolidation (FR-013), every `Valid` row becomes a `Participant` (deduplicated by email within
the competition, reusing an existing row) + `BeerEntry` (with a generated unique `BlindCode`) +
`EntryCollaborator` rows; `Excluded` rows are simply skipped.

### Judge *(competition-scoped judge profile)*

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| CompetitionId | Guid | FK → Competition; **unique (CompetitionId, Email)** |
| Email | string(320) | required; COI matching key vs Participant.Email + EntryCollaborator.Email |
| KeycloakUserId | string? | set once provisioned in Keycloak |
| DisplayName | string(200) | defaults to email local-part until first login |

### Invitation

| Field | Type | Constraints |
|-------|------|-------------|
| Id | Guid | PK |
| JudgeId | Guid | FK → Judge |
| Status | enum | `Pending` \| `Sent` \| `Failed` |
| Attempts | int | default 0 |
| LastError | string? | last SMTP failure |
| SentAt | DateTimeOffset? | |

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
| Type | enum | `GeneratePdfs` \| `BundleZip` \| `SendResultEmail` \| `SendInvitation` |
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
JudgeSampleDto { BeerEntryId, BlindCode, StyleCode, StyleName, SequenceOrder?, EvaluationStatus }
```

`BeerName`, `Participant.*`, `EntryCollaborator.*` are never referenced by any judge-facing
query. Contract tests assert the serialized payloads contain none of these fields.

## Client-side stores (Dexie / IndexedDB — R-08)

| Store | Key | Contents |
|-------|-----|----------|
| `drafts` | `beerEntryId` | in-progress sheet fields; written ≤300 ms after each change (SC-003); deleted on successful submit |
| `outbox` | `idempotencyKey` | submitted-but-unsynced evaluation payloads + attempt metadata; replayed per R-08. Secondary index on `tastingTableId` (T020) — T087 looks up "outbox items for this table" when a judge is removed |

Client stores are caches, never sources of truth; a device wipe loses only unsynced work, which
the UI surfaces via the offline badge (FR-027).

## Relationship overview

```text
Competition 1─* Participant 1─* BeerEntry *─1 BjcpStyle
Competition 1─* ImportBatch 1─* ImportRow (staging; consolidates into Participant/BeerEntry/EntryCollaborator)
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
