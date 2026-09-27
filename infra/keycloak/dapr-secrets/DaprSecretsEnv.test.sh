#!/usr/bin/env bash
# Tests of the Keycloak Dapr secret loader (T142): compiles DaprSecretsEnv.java and runs it against
# a stub of the Dapr sidecar's secrets API. Needs a JDK (21+) and python3.
#   bash infra/keycloak/dapr-secrets/DaprSecretsEnv.test.sh
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'kill "${stub_pid:-}" 2>/dev/null || true; rm -rf "$work"' EXIT

javac --release 21 -d "$work" "$here/DaprSecretsEnv.java"

port=$((20000 + RANDOM % 20000))
cat >"$work/stub.py" <<'PY'
import http.server, json, sys
# Secret values exercise JSON escapes and shell quoting.
SECRETS = {
    "keycloak-db-password": "p@ss'word\"with\\escapes",
    "Smtp--Password": "unicode-é-<tag>&",
}
class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        prefix = "/v1.0/secrets/secretstore/"
        name = self.path[len(prefix):] if self.path.startswith(prefix) else None
        if name not in SECRETS:
            self.send_response(500); self.end_headers(); self.wfile.write(b'{"errorCode":"ERR_SECRET_GET"}'); return
        body = json.dumps({name: SECRETS[name]}).encode()  # ensure_ascii escapes -> é
        self.send_response(200); self.send_header("Content-Type", "application/json"); self.end_headers(); self.wfile.write(body)
    def log_message(self, *args): pass
http.server.HTTPServer(("127.0.0.1", int(sys.argv[1])), Handler).serve_forever()
PY
python3 "$work/stub.py" "$port" & stub_pid=$!
for _ in $(seq 50); do (echo >"/dev/tcp/127.0.0.1/$port") 2>/dev/null && break; sleep 0.1; done

run() { java -cp "$work" DaprSecretsEnv; }
fail() { echo "FAIL: $*" >&2; exit 1; }

# 1. Without DAPR_SECRET_STORE nothing is printed (local runs keep plain env vars).
out="$(env -u DAPR_SECRET_STORE DAPR_SECRETS='A=keycloak-db-password' DAPR_HTTP_PORT="$port" java -cp "$work" DaprSecretsEnv)"
[[ -z "$out" ]] || fail "expected no output without DAPR_SECRET_STORE, got: $out"

# 2. Secrets are exported verbatim, whatever quotes/escapes/unicode they contain.
out="$(DAPR_SECRET_STORE=secretstore DAPR_HTTP_PORT="$port" \
    DAPR_SECRETS='KC_DB_PASSWORD=keycloak-db-password, SMTP_PASSWORD=Smtp--Password' run)"
eval "$out"
[[ "$KC_DB_PASSWORD" == "p@ss'word\"with\\escapes" ]] || fail "KC_DB_PASSWORD round-trip: $KC_DB_PASSWORD"
[[ "$SMTP_PASSWORD" == "unicode-é-<tag>&" ]] || fail "SMTP_PASSWORD round-trip: $SMTP_PASSWORD"

# 3. A secret that never becomes readable fails (non-zero) after the timeout, printing nothing.
set +e
out="$(DAPR_SECRET_STORE=secretstore DAPR_HTTP_PORT="$port" DAPR_SECRETS_TIMEOUT_SECONDS=0 \
    DAPR_SECRETS='X=missing-secret' run 2>/dev/null)"
status=$?
set -e
[[ $status -ne 0 ]] || fail "expected a failure for a missing secret"
[[ -z "$out" ]] || fail "expected no exports on failure, got: $out"

# 4. A malformed mapping is rejected.
if DAPR_SECRET_STORE=secretstore DAPR_HTTP_PORT="$port" DAPR_SECRETS='not-a-mapping' run >/dev/null 2>&1; then
    fail "expected a malformed DAPR_SECRETS to be rejected"
fi

echo "DaprSecretsEnv: all tests passed"
