#!/usr/bin/env bash
# Tests of the entrypoint fail-fast on a missing API_ADMIN_CLIENT_SECRET (T130). The real kc.sh is
# replaced by a stub (KC_BIN) that records it was reached.
#   bash infra/keycloak/dapr-secrets/ApiAdminSecretGuard.test.sh
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
fail() { echo "FAIL: $*" >&2; exit 1; }

printf '#!/bin/bash\necho STARTED "$@"\n' >"$work/kc.sh"; chmod +x "$work/kc.sh"
run() { env -u DAPR_SECRET_STORE -u API_ADMIN_CLIENT_SECRET "$@" KC_BIN="$work/kc.sh" bash "$here/docker-entrypoint.sh" start 2>&1; }

for value in "" "   "; do
    set +e; out="$(run API_ADMIN_CLIENT_SECRET="$value")"; status=$?; set -e
    [[ $status -ne 0 ]] || fail "expected a failure for API_ADMIN_CLIENT_SECRET='$value'"
    [[ "$out" != *STARTED* ]] || fail "Keycloak must not start without the secret"
    [[ "$out" == *API_ADMIN_CLIENT_SECRET* ]] || fail "message must name the variable: $out"
done

set +e; out="$(run)"; status=$?; set -e
[[ $status -ne 0 && "$out" != *STARTED* && "$out" == *"not set or empty"* ]] || fail "unset variable must fail with the guard message: $out"

out="$(run API_ADMIN_CLIENT_SECRET=s3cr3t-value)"
[[ "$out" == "STARTED start" ]] || fail "expected start with a secret, got: $out"
[[ "$out" != *s3cr3t-value* ]] || fail "secret value printed"

# The guard also covers a secret supplied by the Dapr loader (java stubbed on PATH).
mkdir "$work/bin"
printf '#!/bin/bash\necho "export API_ADMIN_CLIENT_SECRET=from-dapr"\n' >"$work/bin/java"; chmod +x "$work/bin/java"
out="$(PATH="$work/bin:$PATH" run DAPR_SECRET_STORE=x)"
[[ "$out" == "STARTED start" ]] || fail "secret loaded through Dapr must satisfy the guard: $out"

echo "ApiAdminSecretGuard: all tests passed"
