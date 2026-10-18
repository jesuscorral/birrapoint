---
description: Audit judge-facing flows for offline correctness and entrant-identity leaks
---

Run the `offline-sync` and `blind-tasting-integrity` skills, one after the other, over the current
code (each skill lists the paths it covers).

Merge both reports into one table: `file:line | issue | severity (blocker/warning) | fix`.
Report only; fix blockers after I confirm.
