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
   "tests first, verified failing". The task id goes in the branch, commits and PR only — tell
   agents not to write it in code, comments or test names (cite FR-/US/ADR or the reason). Run independent agents in parallel; qa-engineer writes the
   contract/E2E tests the task assigns before feature code lands. Read each report; re-brief on
   gaps instead of patching their code yourself.
5. **Gates**: run the gates of every stack touched (CLAUDE.md §Commands; infra gates in the
   infra-engineer agent). If one can't run, record why.
6. **Docs**: launch `docs-keeper` with the task id and branch (living doc, ADR if a significant
   decision, drift check, mark the task `[X]` only if fully delivered).
7. **PR**: semantic commit(s), push, then `gh pr create --base main --title "<type>(<scope>):
   <subject> (<task id>)" --body-file <file>`. Build the body from
   `.github/pull_request_template.md`: fill every section with real data (requirements, reading
   order, invariants touched, gate results, anything not verified, risks), delete the areas and
   the screenshots block the change doesn't use, keep no `<!-- -->` hints. When most files are
   moves or generated, say so under "How to review" and how to list the rest. pr.yml rejects a
   non-Conventional title or an empty "What and why". Then run `/review-pr <n>`.
8. **Reply**: PR link, review verdict with its Blocker/Major findings, and what still needs a human.
   Fix findings only when asked (`/address-review <n>`).
