#!/usr/bin/env bash
# Tests for .github/scripts/pr-size.sh. Run: bash .github/scripts/pr-size.test.sh
set -u
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fail=0

# check <changed lines> <expected label or "error">
check() {
  local out status=0
  out="$(bash "$here/pr-size.sh" "$1" 2>/dev/null)" || status=$?
  if [ "$2" = error ]; then
    if [ "$status" -eq 0 ]; then echo "FAIL $1: expected an error, got '$out'"; fail=1; else echo "ok   $1 -> error"; fi
    return
  fi
  if [ "$status" -ne 0 ] || [ "$out" != "$2" ]; then echo "FAIL $1: got '$out' (exit $status), expected '$2'"; fail=1; else echo "ok   $1 -> $2"; fi
}

check 0 size/XS
check 9 size/XS
check 10 size/S
check 99 size/S
check 100 size/M
check 399 size/M
check 400 size/L
check 999 size/L
check 1000 size/XL
check -1 error
check abc error
check '' error

exit "$fail"
