#!/usr/bin/env bash
# Tests for .github/scripts/check-no-task-ids.sh. Run: bash .github/scripts/check-no-task-ids.test.sh
set -uo pipefail

script="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/check-no-task-ids.sh"
failures=0
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

git -C "$tmp" init -q
mkdir -p "$tmp/src/Birrapoint.Web" "$tmp/tests" "$tmp/docs"

assert_passes() { # <description>
  if bash "$script" "$tmp" >/dev/null 2>&1; then
    echo "ok   - $1"
  else
    echo "FAIL - $1: expected exit 0"
    failures=$((failures + 1))
  fi
}

assert_fails() { # <description>
  if bash "$script" "$tmp" >/dev/null 2>&1; then
    echo "FAIL - $1: expected a non-zero exit"
    failures=$((failures + 1))
  else
    echo "ok   - $1"
  fi
}

printf '// Deny-by-default fallback policy (FR-005, ADR-0009).\n' > "$tmp/src/Clean.cs"
printf 'const stamp = "2026-07-09T090754Z"; // T-shaped, not a task id\n' > "$tmp/src/Birrapoint.Web/clean.ts"
printf 'Pending work lives in T092.\n' > "$tmp/docs/tasks-note.md"
assert_passes 'clean files and ids outside the scanned folders pass'

printf '// JWT bearer against Keycloak (T011).\n' > "$tmp/src/Bad.cs"
assert_fails 'an id in a backend comment fails'
rm "$tmp/src/Bad.cs"

printf '[Fact] // Regression for T140
' > "$tmp/tests/BadTests.cs"
assert_fails 'an id in a backend test name fails'
rm "$tmp/tests/BadTests.cs"

printf '<Project><!-- T042 --></Project>
' > "$tmp/Directory.Build.props"
assert_fails 'an id in a root build file fails'
rm "$tmp/Directory.Build.props"

printf 'describe("pool filters (T125b)", () => {});\n' > "$tmp/src/Birrapoint.Web/bad.spec.ts"
assert_fails 'an id with a letter suffix in a frontend test name fails'
rm "$tmp/src/Birrapoint.Web/bad.spec.ts"

assert_passes 'passes again once the offending files are gone'

if [ "$failures" -gt 0 ]; then
  echo "$failures test(s) failed"
  exit 1
fi
