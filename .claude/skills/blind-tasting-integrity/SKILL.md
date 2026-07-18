---
name: "blind-tasting-integrity"
description: "Audit judge-facing code paths for entrant-identity leaks (BR-01/FR-019) and missing authorization on judge/organizer boundaries — DTOs, SignalR payloads, endpoints, frontend judge views."
user-invocable: true
disable-model-invocation: false
---

# Blind Tasting Integrity Audit

BirraPoint's core security invariant (BR-01/FR-019, CLAUDE.md §Non-negotiable invariants #1):
judge-facing DTOs and SignalR payloads must **physically lack** entrant-identifying fields — no
beer name, participant, brewery, or origin. Judges see only blind code + declared style. This is
a structural guarantee, not a UI-hiding guarantee: a field the client merely doesn't render is
still a leak if it's present in the payload (visible in devtools/network tab, cached in
IndexedDB, logged).

The allowed judge-facing shape is defined in `specs/001-birrapoint-mvp/data-model.md`
§Anonymity (`JudgeSampleDto` et al.) and `specs/001-birrapoint-mvp/contracts/rest-api.md` /
`contracts/signalr-hub.md`. Treat those as the source of truth for what's permitted — this audit
checks the code matches them, not the other way around.

## Steps

1. **Backend DTOs.** Grep `backend/src/BirraPoint.Api/Features/TastingOrder/`,
   `Features/Evaluations/`, and any other slice that projects data toward `/me/**` judge
   endpoints or judge-scoped SignalR groups. For every response type, list its fields against
   `data-model.md` §Anonymity. Flag any field not explicitly allowed there — especially anything
   named `*Name`, `Participant*`, `Brewer*`, `Entrant*`, `Origin`, `Owner*` on a type that
   reaches a judge.
2. **SignalR payloads.** Grep `Realtime/` and every `.Clients.Group("table:{tableId}")` /
   judge-scoped hub emit for the event payload shape. Same field check as above — judges are
   members of `table:{tableId}` groups, so anything broadcast there is judge-visible.
3. **Endpoint authorization.** For every judge-facing endpoint (`/me/**`, `/api/v1/tables/{id}/**`
   from the judge side), confirm a `JUDGE` policy + membership/ownership check is present —
   deny-by-default per CLAUDE.md §Security. A judge must never be able to fetch another judge's
   table or a table outside their assignment (expect `404`, not `403`, per the "never reveal
   existence outside caller's scope" rule).
4. **Frontend judge views.** Grep `frontend/src/app/features/judge-tables/`,
   `features/evaluation-sheet/`, and `core/offline/` (Dexie schemas) for any field or API call
   requesting entrant data. Check Dexie-cached judge data doesn't accidentally persist a field
   the backend never should have sent (defense in depth, but also a sign the backend leaked it).
5. **Contract cross-check.** Confirm `contracts/rest-api.md` and `contracts/signalr-hub.md`
   actually document the judge-facing shapes you found — if the code has a field the contract
   doesn't mention (or vice versa), that's contract drift (Principle VI), not just an anonymity
   issue.

## Report format

Table: `file:line | field/endpoint | issue | severity (blocker/warning) | fix`.

Blockers = any entrant-identifying field reachable by a judge, or a missing/incorrect
authorization check on a judge-facing boundary. Everything else (naming, drift docs-vs-code
without an actual leak) is a warning.

Do not fix blockers automatically — report first, fix only after confirmation, since a fix here
usually touches a DTO or contract shape that other code depends on.
