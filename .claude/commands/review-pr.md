---
description: Run senior-code-reviewer on a PR and post the findings as an informational comment
argument-hint: <PR number>
---

Review PR #$ARGUMENTS (workflow step 5).

1. Launch the `senior-code-reviewer` agent with: "Review PR #$ARGUMENTS in this repo." plus the task
   id and a one-line summary of what the PR delivers (from `gh pr view $ARGUMENTS`).
2. Post the agent's output (without its `### Inline` block) unchanged as a PR comment:
   `gh pr comment $ARGUMENTS --body-file <tmp file>` (write the body to a temp file in the
   scratchpad; never approve or request changes).
3. For each entry of the `### Inline` block, post a standalone review comment on that line (a
   comment, not a review: the `/reviews` endpoint stays blocked by guard.js):
   `gh api repos/{owner}/{repo}/pulls/$ARGUMENTS/comments -f commit_id=<head sha> -f path=<path>
   -F line=<line> -f side=RIGHT -f body=<body>` (head sha from `gh pr view $ARGUMENTS --json
   headRefOid`). If GitHub rejects a line (outside the diff, 422), skip it; the finding is
   already in the summary comment.
4. Reply with the verdict and the Blocker/Major findings in one short list. Do not fix anything
   unless asked.
