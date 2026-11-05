#!/usr/bin/env bash
# Pull request hygiene: a Conventional Commits title (a task id in parentheses at the end is allowed)
# and a filled "## What and why" section from .github/pull_request_template.md. Bots (Dependabot)
# are skipped. Reads PR_TITLE, PR_BODY and PR_AUTHOR from the environment (never interpolate them
# into the script: they are user-controlled). Run by pr.yml.
set -uo pipefail

title="${PR_TITLE:-}"
body="${PR_BODY:-}"
author="${PR_AUTHOR:-}"

if [[ "$author" == *"[bot]" ]]; then
  echo "skipped - $author is a bot"
  exit 0
fi

fail=0
types='feat|fix|refactor|perf|docs|test|build|ci|chore|style|revert'
if ! grep -Eq "^($types)(\([a-z0-9._/-]+\))?!?: [^ ].*" <<<"$title"; then
  echo "::error::PR title must follow Conventional Commits: '<type>(<scope>): <subject>' with type one of ${types//|/, } (got: '$title')."
  fail=1
fi
if [ "${#title}" -gt 100 ]; then
  echo "::error::PR title is ${#title} characters; keep it within 100."
  fail=1
fi

# Text of the "What and why" section without HTML comments (the template's hints) or blank lines.
section="$(printf '%s' "$body" | tr -d '\r' | perl -0777 -ne '
  s/<!--.*?-->//gs;
  print $1 if /^##[ \t]+What and why[ \t]*\n(.*?)(?=^##[ \t]|\z)/ms;
' | grep -v '^[[:space:]]*$' || true)"
if [ -z "$section" ]; then
  echo "::error::PR description needs a filled '## What and why' section (see .github/pull_request_template.md)."
  fail=1
fi

[ "$fail" -eq 0 ] && echo "ok - PR title and description"
exit "$fail"
