---
description: Deliver one tasks.md task end to end — branch, tollgate, TDD via agents, PR, review, docs
argument-hint: <task-id, e.g. T139>
---

Deliver task $ARGUMENTS following CLAUDE.md §Workflow. The main session orchestrates; the code is
written by the owning agents.

1. **Read**: `git fetch`, then the task entry in `origin/main:specs/001-birrapoint-mvp/tasks.md`
   plus the spec/contract/quickstart sections it cites. If it's already `[X]`, or it needs a product
   decision the spec doesn't make, stop and say so (requirement changes go to the spec first).
2. **Branch**: `git switch -c feature/$ARGUMENTS origin/main` (reuse it if it exists).
3. **Tollgate**: files to create/modify, approach, test strategy, which agent owns each part
   (backend-, frontend-, qa-, infra-engineer). **Stop** until the user replies "Approved, proceed".
4. **Implement**: brief each agent with the task id, its files, the contract/quickstart refs and
   "tests first, verified failing". Run independent agents in parallel; qa-engineer writes the
   contract/E2E tests the task assigns before feature code lands. Read each report; re-brief on
   gaps instead of patching their code yourself.
5. **Gates**: run the gates of every stack touched (CLAUDE.md §Commands; infra gates in the
   infra-engineer agent). If one can't run, record why.
6. **Docs**: launch `docs-keeper` with the task id and branch (living doc, ADR if a significant
   decision, drift check, mark the task `[X]` only if fully delivered).
7. **PR**: semantic commit(s), push, `gh pr create --base main` with summary, test evidence and
   anything not verified. Then run `/review-pr <n>`.
8. **Reply**: PR link, review verdict with its Blocker/Major findings, and what still needs a human.
   Fix findings only when asked (`/address-review <n>`).
