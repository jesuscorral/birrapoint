#!/usr/bin/env bash
# Tests for .github/scripts/check-pr.sh. Run: bash .github/scripts/check-pr.test.sh
set -u
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/check-pr.sh"
fail=0

good_body=$'## What and why\n\nAdds the PR template (FR-064).\n\n## How to review\n\n<!-- order -->\n'

# check <name> <expected exit> <must-match regex or empty> <title> <body> [author]
check() {
  local name="$1" want="$2" must="$3" out status=0
  out="$(PR_TITLE="$4" PR_BODY="$5" PR_AUTHOR="${6:-alice}" bash "$script" 2>&1)" || status=$?
  if [ "$status" -ne "$want" ]; then echo "FAIL $name: exit $status, expected $want: $out"; fail=1; return; fi
  if [ -n "$must" ] && ! grep -Eq "$must" <<<"$out"; then echo "FAIL $name: output lacks /$must/: $out"; fail=1; return; fi
  echo "ok   $name"
}

check "conventional title and filled body pass" 0 '' 'feat(ci): add PR template' "$good_body"
# Built at runtime: a literal id here would trip check-no-task-ids.sh.
ids="T$((150 + 1)), T$((150 + 2))"
check "breaking-change marker and task ids are allowed" 0 '' "refactor(modules)!: split contracts ($ids)" "$good_body"
check "scope is optional" 0 '' 'chore: bump tools' "$good_body"
check "CRLF body passes" 0 '' 'fix: x' $'## What and why\r\n\r\nReal text.\r\n'
check "non-conventional title fails" 1 '::error::.*title' 'Add rateLimiting' "$good_body"
check "unknown type fails" 1 '::error::.*title' 'feature: add x' "$good_body"
check "missing subject fails" 1 '::error::.*title' 'fix: ' "$good_body"
check "title over 100 chars fails" 1 '::error::.*100' "feat: $(printf 'x%.0s' {1..100})" "$good_body"
check "empty body fails" 1 '::error::.*What and why' 'fix: x' ''
check "missing section fails" 1 '::error::.*What and why' 'fix: x' $'## Summary\n\nText.\n'
check "untouched template fails" 1 '::error::.*What and why' 'fix: x' $'## What and why\n<!-- 2-3 sentences -->\n\n## How to review\nfoo\n'
check "multi-line comment only fails" 1 '::error::.*What and why' 'fix: x' $'## What and why\n<!--\nhint\n-->\n## Changes\n'
check "both problems are reported" 1 'title.*|What and why' 'Bad title' ''
check "the untouched repository template fails" 1 '::error::.*What and why' 'fix: x' "$(cat "$here/../pull_request_template.md")"
filled="$(awk '{ print } /^## What and why$/ { print "Adds the template (FR-064)." }' "$here/../pull_request_template.md")"
check "the repository template once filled passes" 0 '' 'fix: x' "$filled"
check "bots are skipped" 0 'skipped' 'Bump x from 1 to 2' '' 'dependabot[bot]'

exit "$fail"
