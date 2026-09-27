---
description: Audit judge-facing flows for offline correctness and entrant-identity leaks
---

Run the `offline-sync` and `blind-tasting-integrity` skills, one after the other, over the current
code (`frontend/src/app/core/offline/`, `features/evaluation-sheet/`, `features/judge-tables/`,
`features/discrepancy/`; backend `Features/TastingOrder/`, `Features/Evaluations/`, `Realtime/`).

Merge both reports into one table: `file:line | issue | severity (blocker/warning) | fix`.
Report only; fix blockers after I confirm.
