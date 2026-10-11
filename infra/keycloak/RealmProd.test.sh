#!/usr/bin/env bash
# Tests of the production realm derivation (T130): applies realm-prod.jq (the filter the Dockerfile
# `realm` stage uses) to the local-dev realm and asserts the hardening. Needs jq (or Docker, which
# runs jq from an alpine image) and python3.
#   bash infra/keycloak/RealmProd.test.sh
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
fail() { echo "FAIL: $*" >&2; exit 1; }

if command -v jq >/dev/null 2>&1; then
    jq "$(cat "$here/realm-prod.jq")" "$here/birrapoint-realm.json" >"$work/prod.json"
else
    docker run --rm -i alpine:3.22 sh -c 'apk add --no-cache -q jq && jq "$1"' _ "$(cat "$here/realm-prod.jq")"         <"$here/birrapoint-realm.json" >"$work/prod.json"
fi

PY="$(command -v python3 || command -v python)"
"$PY" - "$work/prod.json" "$here/birrapoint-realm.json" <<'PY' || fail "production realm assertions"
import json, sys
prod = json.load(open(sys.argv[1])); dev = json.load(open(sys.argv[2]))
def check(cond, msg):
    if not cond:
        print("FAIL: " + msg, file=sys.stderr); sys.exit(1)

check(prod.get("bruteForceProtected") is True, "bruteForceProtected must be true")
check(prod.get("permanentLockout") is False, "permanentLockout must be false (DoS on organizers)")
check(prod.get("failureFactor", 0) >= 3 and prod.get("failureFactor", 99) <= 10, "failureFactor 3..10")
check(prod.get("waitIncrementSeconds", 0) > 0 and prod.get("maxFailureWaitSeconds", 0) >= 60, "lockout wait settings")
policy = prod.get("passwordPolicy", "")
for part in ("length(12)", "notUsername", "notEmail"):
    check(part in policy, "passwordPolicy lacks " + part + ": " + repr(policy))
# The API's temporary password is 24 random base64 chars: no character-class rule may be required.
for bad in ("upperCase", "lowerCase", "digits", "specialChars"):
    check(bad not in policy, "passwordPolicy must not require " + bad + " (temp password is random base64)")
check(prod.get("verifyEmail") is True, "verifyEmail must be true (self-registration grants ORGANIZER)")
check(prod.get("registrationAllowed") is True, "registration must stay enabled")
check(not [u for u in prod["users"] if u["username"] == "organizer"], "seed organizer must be removed")
check([u for u in prod["users"] if u.get("serviceAccountClientId") == "birrapoint-api-admin"], "service-account user must stay")
text = open(sys.argv[1]).read()
check("dev-only-secret" not in text, "dev secret leaked into production realm")
api = [c for c in prod["clients"] if c["clientId"] == "birrapoint-api-admin"][0]
check(api["secret"] == "${API_ADMIN_CLIENT_SECRET}", "api admin secret must come only from API_ADMIN_CLIENT_SECRET")
# The local-dev realm must stay as is (Aspire/E2E log in with the seeded organizer, no email checks).
check(not dev.get("verifyEmail") and not dev.get("bruteForceProtected") and "passwordPolicy" not in dev, "dev realm must not be hardened")
PY
echo "RealmProd: all tests passed"
