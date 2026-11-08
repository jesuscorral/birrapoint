#!/usr/bin/env bash
# Fails when a spec-kit task id (T001, T125b, ... from specs/*/tasks.md) appears in backend (src, tests, root
# build files), frontend (src/Birrapoint.Web), infra or .github files.
# Task ids belong in specs/*/tasks.md and commit messages; in code they point at a tracker
# that outlives neither the code nor the reader. Cite the requirement (FR-/SC-/US/ADR-) or state
# the reason instead.
# Usage (repo root by default): bash .github/scripts/check-no-task-ids.sh [repo-dir]
set -uo pipefail

cd "${1:-$(git rev-parse --show-toplevel)}" || exit 2

hits="$(git grep --untracked -nIEw 'T[0-9]{3}[A-Za-z]?' -- \
  src tests infra .github \
  BirraPoint.sln Directory.Build.props Directory.Packages.props .editorconfig .dockerignore \
  ':!*package-lock.json' \
  ':!.github/scripts/check-no-task-ids.sh' \
  ':!.github/scripts/check-no-task-ids.test.sh')"

if [ -n "$hits" ]; then
  echo "Task ids (T###) found in code. Cite FR-/SC-/US/ADR or explain the reason instead:" >&2
  echo "$hits" >&2
  exit 1
fi
echo "ok - no task ids in src (incl. the PWA), tests, infra or .github"
