---
description: Fix the findings of the senior-code-reviewer comment on a PR, re-run gates and push
argument-hint: <PR number> [finding ids, e.g. B1 M2; default all Blocker + Major]
---

Address the review on PR $ARGUMENTS.

1. `gh pr view <n> --json headRefName,comments` and find the latest `## Review — PR #<n>` comment.
   Switch to the PR branch (`git switch <headRefName>`, `git pull`).
2. Select the findings: the ids given in the arguments, otherwise every Blocker and Major. List
   them with the agent that owns each file (backend-, frontend-, qa-, infra-engineer). Minors only
   if asked.
3. Verify each finding against the code before fixing; if one is wrong or needs a spec decision,
   say so instead of changing code.
4. Delegate the fixes (test first when the finding is a bug: a test that fails without the fix).
5. Run the gates of the stacks touched; if behavior or a decision changed, run `docs-keeper`.
6. Commit `fix(<scope>): address PR #<n> review (<task-id>)`, push, and reply with a short
   finding → fix (or "not changed, because …") list. Don't re-request review or merge.
