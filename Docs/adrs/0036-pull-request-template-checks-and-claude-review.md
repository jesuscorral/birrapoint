# 0036 - Pull request template, PR checks and automated Claude review

**Status:** Accepted
**Date:** 2026-11-05

## Context
PR descriptions were free text written by `/deliver` or by hand, with uneven structure. Large PRs (PR #89 moved 724 files) did not say which files needed reading, and reviewers had to dig for test evidence and invariants touched. The senior-code-reviewer output was one long comment: verdict at the bottom, findings far from the lines they refer to. Reviews only ran from a local Claude Code session (`/review-pr`).

## Decision
- **Template**: `.github/pull_request_template.md` (English): "What and why" (requirements cited), "How to review" (reading order, mechanical vs key files), changes per area, invariants touched (CLAUDE.md non-negotiables), gate evidence, risks and rollback, collapsed screenshots/logs. `/deliver` builds every PR body from it with `--body-file`.
- **Checks and labels** (`pr.yml`, `pull_request_target`): `check-pr.sh` fails on a non-Conventional Commits title (task ids allowed at the end, as before) or an empty "What and why" (template hints in `<!-- -->` don't count); bots are skipped. `actions/labeler` (SHA-pinned) applies area labels from `.github/labeler.yml`; `pr-size.sh` applies one `size/XS…XL` label from additions + deletions. `pull_request_target` lets fork PRs be labeled; the workflow runs only the base branch's code and passes title/body through environment variables, never interpolated into scripts.
- **Review format**: senior-code-reviewer puts the verdict first and collapses "Verified OK"; `/review-pr` also posts each Blocker/Major as a line comment through `POST /pulls/{n}/comments`. The `/reviews` endpoint stays blocked by `guard.js`, so a review can never approve or request changes.
- **CI review** (`claude-review.yml`): `anthropics/claude-code-action` (SHA-pinned) reviews PRs to `main` when opened, reopened or marked ready, and on manual dispatch (same-repository PRs only), with the same agent rules and comment format. Tools are limited to reading the repo, `git diff/log/show`, `gh pr view/diff/comment` and inline comments. It is opt-in: without `CLAUDE_CODE_OAUTH_TOKEN` or `ANTHROPIC_API_KEY` the job only prints a notice (setup in `infra/ci-setup.md` §3).

## Consequences
- Every PR has the same sections, the reading order and evidence are explicit, and labels allow filtering by area and size.
- Hand-made PRs must follow the title convention and fill "What and why", or `PR / check` fails. It is not a required check unless a ruleset makes it one.
- The CI review costs tokens per opened PR (subscription quota or API billing); re-reviews after new commits are manual to keep that bounded. The repository is public: fork PRs get no secrets, and the action only runs for authors with write access; manual re-reviews refuse fork PRs, because the action restores `.claude/` and `CLAUDE.md` from the base branch only for `pull_request` events and a dispatched fork checkout would run the fork's Claude hooks with the secret present.
- Two more third-party actions with write tokens; both are pinned by SHA and kept current by Dependabot.
- Rejected: `amannn/action-semantic-pull-request` and similar (a small script covers the rule and is tested like the other `.github/scripts`); posting reviews with `event: COMMENT` (would require relaxing the guard against approvals); reviewing on every push (cost).
