---
description: Audit judge-facing flows for offline correctness and identity leaks
---

Load the offline-sync and blind-tasting-integrity skills, then audit:

1. Grep judge-facing frontend flows for direct HttpClient calls that bypass SyncQueueService.
2. Grep `Birrapoint.Contracts` judge DTOs and `/api/judge/*` endpoints for entrant identity fields (brewer, entrant, email, entry name).
3. Check every judge endpoint has role + flight-assignment authorization.
4. Verify pending ops persist in IndexedDB (not memory) and are idempotent.

Report findings as a table: file:line, issue, severity, suggested fix. Fix [blocker] items after I confirm.
