---
description: Deliver one GitHub issue end to end — spec-driven (specify, clarify, plan, tasks), TDD via agents, PR, review, docs
argument-hint: <issue number, e.g. 42>
---

Deliver issue #$ARGUMENTS following CLAUDE.md §Workflow. The main session orchestrates; code is
written by the owning agents. `NNN` = the issue number zero-padded to 3 digits.

1. **Read**: `git fetch`, `gh issue view $ARGUMENTS --json number,title,body,state,labels`. Stop if
   it is closed, or "What and why" / "Done when" are empty (ask the user to complete the issue).
2. **Branch**: pick a 2-4 word kebab `<slug>`; `git switch -c feature/$ARGUMENTS-<slug> origin/main`
   (reuse it if it exists). `bash .claude/scripts/board-status.sh $ARGUMENTS "In progress"`.
3. **Specify**: read and follow `.claude/skills/speckit-specify/SKILL.md` with the issue title +
   body as the feature description and `SPECIFY_FEATURE_DIRECTORY=specs/NNN-<slug>`. Requirement
   ids continue the global sequence (next free `FR-`/`SC-`/`US` across `specs/*/spec.md`) so code
   citations stay unique. Then follow `.claude/skills/speckit-clarify/SKILL.md`, asking the user.
   **Stop 1**: show the spec summary (stories, requirements, assumptions); wait for the user to
   approve it or ask for changes.
4. **Plan + tasks**: follow `speckit-plan`, then `speckit-tasks` (same folder), then run
   `speckit-analyze` and fix its critical findings. New or changed endpoints/events/entities are
   written into the living contracts — `specs/001-birrapoint-mvp/contracts/` and `data-model.md`
   — before code; the feature plan links them instead of copying.
5. **Tollgate (Stop 2)**: files to create/modify, approach, test strategy, which agent owns each
   part (backend-, frontend-, qa-, infra-engineer). **Stop** until the user replies "Approved,
   proceed".
6. **Implement**: brief each agent with its tasks from `specs/NNN-<slug>/tasks.md`, the files, the
   contract/quickstart refs and "tests first, verified failing". Task ids (T001…) and the issue
   number go in commits/PR only — never in code, comments or test names (cite FR-/SC-/US/ADR or the
   reason). Run independent agents in parallel; qa-engineer writes the contract/E2E tests before
   feature code lands. Read each report; re-brief on gaps instead of patching their code. Mark
   finished tasks `[X]`.
7. **Gates**: every stack touched (CLAUDE.md §Commands; infra gates in the infra-engineer agent).
   If one can't run, record why.
8. **Docs**: `docs-keeper` with the issue number and branch.
9. **PR**: semantic commit(s), push, `gh pr create --base main --title "<type>(<scope>): <subject>
   (#$ARGUMENTS)" --body-file <file>`. Body from `.github/pull_request_template.md` with
   `Closes #$ARGUMENTS`, every section filled with real data, unused areas and hints removed. When
   most files are moves or generated, say so under "How to review". Then
   `bash .claude/scripts/board-status.sh $ARGUMENTS "In review"` and `/review-pr <pr>`.
10. **Reply**: PR link, review verdict with its Blocker/Major findings, what still needs a human.
    Fix findings only when asked (`/address-review <pr>`).
