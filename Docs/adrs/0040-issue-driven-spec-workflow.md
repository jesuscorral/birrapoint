# 0040 - Issue-driven, per-feature spec workflow after the MVP

**Status:** Accepted
**Date:** 2026-11-08

## Context
The MVP (US1–US14) is complete. Until now all work lived in one `specs/001-birrapoint-mvp/tasks.md`
and `/deliver <task-id>` drove one task at a time. That file had become stale (delivered items left
open) and a single growing task list does not fit incremental product work. New work needs a
tracker the team already uses, while keeping Spec-Driven Development (constitution Principle I).

## Decision
- Every feature starts as a GitHub issue created from `.github/ISSUE_TEMPLATE/feature.yml`
  (What and why, Done when, optional notes). The form adds it to project board
  `jesuscorral/10`.
- `/deliver <issue>` creates `feature/<issue>-<slug>` and runs Spec Kit in its own folder
  `specs/NNN-<slug>/` (`NNN` = issue number): specify from the issue → clarify → user approves the
  spec → plan → tasks → analyze → tollgate "Approved, proceed" → TDD via agents → PR with
  `Closes #<issue>`.
- `specs/001-birrapoint-mvp/` stays as the baseline spec. Its `contracts/` and `data-model.md` are
  the living contracts that features amend in place instead of copying.
- New `FR-`/`SC-`/`US` ids continue the global sequence so code citations stay unique.
- The MVP `tasks.md`, its checklist and the Spanish product definition (`Docs/01-…`, `Docs/02-…`)
  were removed. The open MVP items were dropped as tracked work and survive only as known debt in
  `Docs/arquitectura_viva.md`. The `speckit-taskstoissues` and `speckit-converge` skills were
  removed: issues are now the input, not the output.
- Constitution amended to v1.6.0.

## Consequences
- Requirements, discussion and status live in one place (issue + board); the spec folder holds
  the design for that issue only, so reviewing a feature means reading one small folder.
- Two explicit stops (spec approval, tollgate) add latency but keep behaviour decisions with the
  user.
- Contracts stay single-sourced, but a feature PR now touches the baseline folder; reviewers must
  check that contract edits match the feature spec.
- Debt previously tracked as tasks (cloud deploy validation, CI E2E job, SC-010 study,
  judge-email and post-finalize correction events) needs a new issue before anyone works on it.
- Small fixes and chores without new behaviour skip the spec folder (`fix/`/`chore/` branches).
