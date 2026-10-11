#!/usr/bin/env bash
# Tests for .github/scripts/require-dockerhub-read.sh (T131). Run: bash .github/scripts/require-dockerhub-read.test.sh
set -u
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/require-dockerhub-read.sh"
fail=0

# check <name> <expected exit> <must-match regex or empty> <must-not-match regex or empty> [VAR=value...]
check() {
  local name="$1" want="$2" must="$3" never="$4"
  shift 4
  local out status=0
  out="$(env -u DOCKERHUB_USERNAME -u DOCKERHUB_READ_TOKEN "$@" bash "$script" 2>&1)" || status=$?
  if [ "$status" -ne "$want" ]; then echo "FAIL $name: exit $status, expected $want"; fail=1; return; fi
  if [ -n "$must" ] && ! grep -Eq "$must" <<<"$out"; then echo "FAIL $name: output lacks /$must/: $out"; fail=1; return; fi
  if [ -n "$never" ] && grep -Eq "$never" <<<"$out"; then echo "FAIL $name: output leaks /$never/"; fail=1; return; fi
  echo "ok   $name"
}

check "both unset" 1 '::error::.*DOCKERHUB_USERNAME' ''
check "username empty" 1 '::error::.*DOCKERHUB_USERNAME' '' DOCKERHUB_USERNAME= DOCKERHUB_READ_TOKEN=tok-secret-1
check "token blank" 1 '::error::.*DOCKERHUB_READ_TOKEN' 'tok-secret|alice' DOCKERHUB_USERNAME=alice "DOCKERHUB_READ_TOKEN=   "
check "username missing, names only it" 1 '::error::.*DOCKERHUB_USERNAME' 'DOCKERHUB_READ_TOKEN|tok-secret-2' DOCKERHUB_READ_TOKEN=tok-secret-2
check "token missing, names only it" 1 '::error::.*DOCKERHUB_READ_TOKEN' 'DOCKERHUB_USERNAME|alice' DOCKERHUB_USERNAME=alice
check "both set" 0 '' 'tok-secret|alice' DOCKERHUB_USERNAME=alice DOCKERHUB_READ_TOKEN=tok-secret-3

exit "$fail"
