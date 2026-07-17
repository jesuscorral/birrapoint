---
name: "blind-tasting-integrity"
description: "BR-01/FR-019 anonymity boundary — the structural rule that judge-facing DTOs and SignalR payloads physically cannot carry entrant-identifying fields. Load before touching any judge-facing read model, SignalR payload, or its contract test."
user-invocable: true
disable-model-invocation: false
---

# BR-01 anonymity boundary

The rule: judges may see blind code + style only, never beer name, participant, brewery, entrant,
or collaborator identity. This is a **security invariant**, not a UI filter — hiding a field in
the template while the API still returns it is a violation, not a partial fix.

## Structural enforcement

Judge-facing read models MUST be built from a dedicated `Features/<Capability>/JudgeDtos`
namespace. The canonical shape (`data-model.md` §Anonymity boundary):

```text
JudgeSampleDto { BeerEntryId, BlindCode, StyleCode, StyleName, SequenceOrder?, EvaluationStatus }
```

No other properties. `BeerName`, `Participant.*`, `EntryCollaborator.*` must never be referenced
by any judge-facing query — not projected, not joined in, not left on the entity and merely
unused by the frontend.

## Where this applies

`Features/TastingOrder/*`, `Features/Evaluations/*` (judge submit/read paths),
`Features/Monitoring/*` (only the organizer-facing audit drill-down may show identity — judge-
facing progress views may not), any `CompetitionHub` group emit to a `table:{tableId}` group.

## Contract test requirement

Every judge-facing endpoint/hub payload needs a structural test asserting the serialized JSON
contains none of the forbidden field names — not just that the UI doesn't render them. `tasks.md`
T051 is the canonical example; treat it as the template for every subsequent judge-facing endpoint
(T052 GetTableSamples, T058 SubmitEvaluation responses, US9 monitoring, US11 discrepancy views).

## Frontend mirror

Judge-facing Angular routes/services must only ever import the DTO shape above — if a component
needs `beerName`, that's a signal the feature is misdesigned, not a missing field to add.

## Quick verification

```bash
grep -riE "beerName|Participant|EntryCollaborator" backend/src/BirraPoint.Api/Features/*/JudgeDtos
grep -riE "beerName|Participant|EntryCollaborator" frontend/src/app/features/judge-*
```

Both should return nothing.
