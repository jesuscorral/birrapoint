---
description: Run senior-code-reviewer on a PR and post the findings as an informational comment
argument-hint: <PR number>
---

Review PR #$ARGUMENTS (workflow step 5).

1. Launch the `senior-code-reviewer` agent with: "Review PR #$ARGUMENTS in this repo." plus the task
   id and a one-line summary of what the PR delivers (from `gh pr view $ARGUMENTS`).
2. Post the agent's output unchanged as a PR comment: `gh pr comment $ARGUMENTS --body-file <tmp file>`
   (write the body to a temp file in the scratchpad; never approve or request changes).
3. Reply with the verdict and the Blocker/Major findings in one short list. Do not fix anything
   unless asked.
