# SignalR Contract: `CompetitionHub`

**Endpoint**: `/hubs/competition` · **Auth**: Keycloak JWT via `access_token` query param on the
handshake; unauthenticated connections rejected. · **Direction**: server → client only — all
mutations go through the REST API (Principle VI). Clients invoke only the group-join methods
below.

## Client-invokable methods (join/leave)

| Method | Args | Authorization |
|--------|------|---------------|
| `JoinCompetitionAsOrganizer` | `competitionId` | caller has `ORGANIZER` role **and** owns the competition → joins group `competition:{id}:organizers`; else hub exception |
| `JoinTable` | `tableId` | caller is a currently assigned (non-removed) judge of the table → joins group `table:{tableId}`; else hub exception |
| `LeaveTable` / `LeaveCompetition` | ids | always allowed |

Reconnection: clients re-join their groups on `onreconnected`; missed state is recovered by
re-fetching `GET /competitions/{id}/progress` (organizer) or `GET /me/tables/{tableId}/samples`
(judge) — events are notifications, not the source of truth.

## Server → client events

### To `table:{tableId}` (judges — blind payloads only)

| Event | Payload | Trigger |
|-------|---------|---------|
| `TableOrderFixed` | `{ tableId, orderedSamples: [{ beerEntryId, blindCode, sequenceOrder }], fixedByDisplayName }` | order fixed (FR-021); UI reorders ≤1 s and locks ordering |
| `DiscrepancyRaised` | `{ alertId, tableId, blindCode, involvedJudgeIds }` | FR-031; involved judges open the alert UI |
| `DiscrepancyResolved` | `{ alertId, tableId, blindCode }` | totals converged within 7 |
| `TableClosed` | `{ tableId }` | FR-033; judge UI switches to read-only |
| `JudgeRemoved` | `{ tableId, judgeId }` | FR-039; the removed judge's client ejects immediately (server also revokes REST access; clients not receiving the event are still locked out server-side) |

Anonymity guard: payloads to this group are built exclusively from `JudgeSampleDto` projections —
no entrant fields exist on the event types (BR-01/FR-019). Contract tests serialize every event
and assert the absence of `beerName`/participant fields.

### To `competition:{id}:organizers`

| Event | Payload | Trigger |
|-------|---------|---------|
| `EvaluationCompleted` | `{ tableId, blindCode, judgeDisplayName, tableProgress: { completed, expected, percent } }` | each confirmed/provisional submission (FR-037, ≤1 s) |
| `TableOrderFixed` | same as judge event | order fixed |
| `DiscrepancyRaised` / `DiscrepancyResolved` | as above | consensus monitoring |
| `TableClosed` | `{ tableId, consolidatedScores: [{ blindCode, mean }] }` | FR-033 + FR-042 |
| `JudgeRemoved` | `{ tableId, judgeId, judgeDisplayName }` | confirmation echo for the dashboard |
| `CompetitionStateChanged` | `{ competitionId, state }` | FR-006 transitions |
| `DispatchProgress` | `{ jobType, status, detail? }` | dispatch pipeline milestones (FR-036/FR-041) |

## Delivery semantics

- Events are fire-and-forget, at-most-once; anything that must not be lost is persisted and
  queryable via REST (progress, alerts, dispatch status).
- Event emission happens **after** the owning transaction commits (no phantom updates).
- Adding fields to payloads is non-breaking; removing/renaming requires a spec amendment
  (Principle VI).
