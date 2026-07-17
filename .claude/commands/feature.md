---
description: Full feature loop — plan, implement in parallel, test until green, review
argument-hint: <feature description>
---

Implement this feature end to end: $ARGUMENTS

Follow this loop strictly:

1. **Plan**: explore relevant code, produce a short plan (files to touch, contract changes, offline + blind-tasting implications). If judge-facing, load the offline-sync and blind-tasting-integrity skills first.
2. **Implement**: if the feature spans backend and frontend, delegate backend to the `dotnet-backend` agent and frontend to the `angular-frontend` agent in parallel. Backend contract first if the frontend depends on it.
3. **Test loop**: run the `test-runner` agent. If anything fails, fix and re-run. Repeat until both suites are green — do not proceed with failures.
4. **Review**: run the `senior-code-reviewer` agent on the diff. Fix all [blocker] findings, then re-review. Repeat until APPROVE.
5. **Finish**: summarize changes, then propose a conventional commit on a feature branch. Do not push.
