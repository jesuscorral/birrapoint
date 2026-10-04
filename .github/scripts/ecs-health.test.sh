#!/usr/bin/env bash
# Tests for .github/scripts/ecs-health.sh (T146). Run: bash .github/scripts/ecs-health.test.sh
# `aws` is a stub on PATH serving canned answers from $STUB_DIR; the waiter is stubbed too.
#   ecs wait ...               -> exit code from $STUB_DIR/wait.exit (default 0), logged in wait.count
#   ecs describe-services ...  -> $STUB_DIR/status.tsv (rows: service<TAB>taskDefinition<TAB>rolloutState<TAB>failedCount)
#                                 or, for the events query, $STUB_DIR/events.txt
set -u
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/ecs-health.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"
cat > "$work/bin/aws" <<'STUB'
#!/usr/bin/env bash
if [ "$1" = "ecs" ] && [ "$2" = "wait" ]; then
  echo x >> "$STUB_DIR/wait.count"
  exit "$(cat "$STUB_DIR/wait.exit" 2>/dev/null || echo 0)"
fi
if [ "$1" = "ecs" ] && [ "$2" = "describe-services" ]; then
  if printf '%s\n' "$*" | grep -q 'events'; then
    cat "$STUB_DIR/events.txt" 2>/dev/null
  else
    [ -f "$STUB_DIR/describe.error" ] && { echo "aws: simulated failure" >&2; exit 255; }
    cat "$STUB_DIR/status.tsv"
  fi
  exit 0
fi
echo "unexpected aws call: $*" >&2
exit 99
STUB
chmod +x "$work/bin/aws"

failures=0
pass() { echo "ok   - $1"; }
fail() { echo "FAIL - $1"; failures=$((failures + 1)); }

new() { export STUB_DIR="$work/$1"; rm -rf "$STUB_DIR"; mkdir -p "$STUB_DIR"; : > "$STUB_DIR/status.tsv"; }
row() { printf '%s\t%s\t%s\t%s\n' "$1" "$2" "$3" "${4:-0}" >> "$STUB_DIR/status.tsv"; }
run() {
  PATH="$work/bin:$PATH" \
    bash "$script" eu-central-1 birrapoint-prod-ecs \
    birrapoint-prod-api=arn:td/api:7 birrapoint-prod-web=arn:td/web:7 birrapoint-prod-kc=arn:td/kc:7 2>&1
}
healthy() {
  row birrapoint-prod-api arn:td/api:7 COMPLETED
  row birrapoint-prod-web arn:td/web:7 COMPLETED
  row birrapoint-prod-kc arn:td/kc:7 COMPLETED
}

new healthy; healthy
out="$(run)"; code=$?
[ "$code" -eq 0 ] && pass "passes when every PRIMARY deployment is COMPLETED on the applied task definition" || fail "healthy: exit $code: $out"

new rolledback; healthy
: > "$STUB_DIR/status.tsv"
row birrapoint-prod-api arn:td/api:6 COMPLETED
row birrapoint-prod-web arn:td/web:7 COMPLETED
row birrapoint-prod-kc arn:td/kc:7 COMPLETED
out="$(run)"; code=$?
{ [ "$code" -ne 0 ] && echo "$out" | grep -q "birrapoint-prod-api" && echo "$out" | grep -q "arn:td/api:6" && echo "$out" | grep -q "arn:td/api:7"; } \
  && pass "fails when the circuit breaker rolled a service back to another task definition" || fail "rolledback: exit $code: $out"

new inprogress; healthy
: > "$STUB_DIR/status.tsv"
row birrapoint-prod-api arn:td/api:7 IN_PROGRESS
row birrapoint-prod-web arn:td/web:7 COMPLETED
row birrapoint-prod-kc arn:td/kc:7 COMPLETED
out="$(run)"; code=$?
{ [ "$code" -ne 0 ] && echo "$out" | grep -q "IN_PROGRESS"; } && pass "fails when the PRIMARY deployment is not COMPLETED" || fail "inprogress: exit $code: $out"

new failedold; healthy
: > "$STUB_DIR/status.tsv"
row birrapoint-prod-api arn:td/api:7 COMPLETED
row birrapoint-prod-web arn:td/web:7 COMPLETED 1
row birrapoint-prod-kc arn:td/kc:7 COMPLETED
out="$(run)"; code=$?
{ [ "$code" -ne 0 ] && echo "$out" | grep -q "birrapoint-prod-web" && echo "$out" | grep -qi "failed"; } && pass "fails when any deployment of a service is FAILED" || fail "failedold: exit $code: $out"

new missing; healthy
: > "$STUB_DIR/status.tsv"
row birrapoint-prod-api arn:td/api:7 COMPLETED
row birrapoint-prod-web arn:td/web:7 COMPLETED
out="$(run)"; code=$?
{ [ "$code" -ne 0 ] && echo "$out" | grep -q "birrapoint-prod-kc"; } && pass "fails when an expected service is not reported" || fail "missing: exit $code: $out"

new events; healthy
: > "$STUB_DIR/status.tsv"
row birrapoint-prod-api arn:td/api:6 COMPLETED
row birrapoint-prod-web arn:td/web:7 COMPLETED
row birrapoint-prod-kc arn:td/kc:7 COMPLETED
echo "service birrapoint-prod-api failed to stabilize" > "$STUB_DIR/events.txt"
out="$(run)"; code=$?
{ [ "$code" -ne 0 ] && echo "$out" | grep -q "failed to stabilize"; } && pass "prints the service events when it fails" || fail "events: exit $code: $out"

new neverstable; healthy
echo 255 > "$STUB_DIR/wait.exit"
out="$(run)"; code=$?
tries="$(wc -l < "$STUB_DIR/wait.count" | tr -d ' ')"
{ [ "$code" -ne 0 ] && [ "$tries" = "3" ] && echo "$out" | grep -qi "did not become stable"; } && pass "retries the waiter 3 times, then fails" || fail "neverstable: exit $code tries $tries: $out"

new describefails; healthy
touch "$STUB_DIR/describe.error"
out="$(run)"; code=$?
[ "$code" -ne 0 ] && pass "fails when describe-services itself fails" || fail "describefails: exit $code: $out"

new usage
out="$(PATH="$work/bin:$PATH" bash "$script" eu-central-1 2>&1)"; code=$?
[ "$code" -eq 2 ] && pass "usage error without services" || fail "usage: exit $code: $out"

echo
if [ "$failures" -eq 0 ]; then echo "All tests passed."; else echo "$failures test(s) failed."; exit 1; fi
