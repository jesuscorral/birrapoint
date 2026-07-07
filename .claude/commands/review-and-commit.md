---
description: Review working changes, fix blockers, commit
---

1. Run the `code-reviewer` agent on the current diff.
2. Fix every [blocker], re-review until APPROVE.
3. Confirm build + tests are green.
4. Create a conventional commit (feat/fix/chore) on the current branch with a concise message. Never commit to main; if on main, create a branch first. Do not push.
