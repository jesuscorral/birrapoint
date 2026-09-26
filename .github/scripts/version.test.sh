#!/usr/bin/env bash
# Tests for .github/scripts/version.sh (T135). Run: bash .github/scripts/version.test.sh
set -uo pipefail

script="$(dirname "${BASH_SOURCE[0]}")/version.sh"
failures=0
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

assert_output() { # <description> <expected> <command...>
  local description="$1" expected="$2" actual
  shift 2
  if actual="$("$@" 2>/dev/null)" && [ "$actual" = "$expected" ]; then
    echo "ok   - $description"
  else
    echo "FAIL - $description: expected '$expected', got '${actual:-<error>}'"
    failures=$((failures + 1))
  fi
}

assert_fails() { # <description> <command...>
  local description="$1"
  shift
  if "$@" >/dev/null 2>&1; then
    echo "FAIL - $description: expected a non-zero exit"
    failures=$((failures + 1))
  else
    echo "ok   - $description"
  fi
}

printf '0.3.1\n' > "$tmp/version.txt"
printf '  1.2.3 \r\n\n' > "$tmp/padded.txt"
printf 'v1.2.3\n' > "$tmp/prefixed.txt"
: > "$tmp/empty.txt"

assert_output 'read returns the version' '0.3.1' bash "$script" read "$tmp/version.txt"
assert_output 'read trims whitespace and CRLF' '1.2.3' bash "$script" read "$tmp/padded.txt"
assert_fails 'read rejects a v-prefixed version' bash "$script" read "$tmp/prefixed.txt"
assert_fails 'read rejects an empty file' bash "$script" read "$tmp/empty.txt"
assert_fails 'read fails on a missing file' bash "$script" read "$tmp/missing.txt"

assert_output 'validate accepts X.Y.Z' '10.20.30' bash "$script" validate 10.20.30
for bad in 1.2 1.2.3.4 01.2.3 1.2.3-rc1 v1.2.3 latest ''; do
  assert_fails "validate rejects '$bad'" bash "$script" validate "$bad"
done

assert_output 'next patch' '0.3.2' bash "$script" next 0.3.1 patch
assert_output 'next minor resets patch' '0.4.0' bash "$script" next 0.3.1 minor
assert_output 'next major resets minor and patch' '1.0.0' bash "$script" next 0.3.1 major
assert_output 'next patch carries past 9' '0.3.10' bash "$script" next 0.3.9 patch
assert_fails 'next rejects an unknown bump' bash "$script" next 0.3.1 build
assert_fails 'next rejects an invalid version' bash "$script" next 0.3 patch

assert_fails 'unknown command fails' bash "$script" frobnicate

if [ "$failures" -gt 0 ]; then
  echo "$failures test(s) failed"
  exit 1
fi
echo 'all tests passed'
