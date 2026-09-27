---
description: Review working changes, fix blockers, run the gates and commit on a feature branch
---

1. If on `main`, create a branch first (`feature/<task-id>` or `fix/<topic>`).
2. Launch `senior-code-reviewer` on the uncommitted diff. Fix every Blocker and Major finding that
   is in scope, then re-review once.
3. Run the gates for the stacks you touched (CLAUDE.md §Commands): backend build/test/format,
   frontend jest/lint/format/build:budget, infra fmt/validate/Pester.
4. Commit with a semantic message (`feat|fix|docs|chore|refactor|test(<scope>): …`). Do not push.
