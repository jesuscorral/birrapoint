#!/usr/bin/env bash
# Tests for .github/scripts/wait-revisions.sh (T143). Run: bash .github/scripts/wait-revisions.test.sh
# `az` is a stub on PATH that serves canned answers from $STUB_DIR; time is virtual (no sleeping).
set -u
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$here/wait-revisions.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"
cat > "$work/bin/az" <<'STUB'
#!/usr/bin/env bash
# containerapp show --name <app> ...            -> <app>.show.<n>      (3 lines: latest, ready, minReplicas)
# containerapp revision show --name <app> ...   -> <app>.revision.<n>  (3 lines: runningState, healthState, replicas)
app=""; kind="show"
args=("$@")
for ((i = 0; i < ${#args[@]}; i++)); do
  [ "${args[$i]}" = "--name" ] && app="${args[$((i + 1))]}"
  [ "${args[$i]}" = "revision" ] && kind="revision"
done
counter="$STUB_DIR/.count.$app.$kind"
n=$(( $(cat "$counter" 2>/dev/null || echo 0) + 1 )); echo "$n" > "$counter"
file="$STUB_DIR/$app.$kind.$n"
[ -f "$file" ] || file="$(ls "$STUB_DIR/$app.$kind."* | sort -V | tail -n 1)"
if head -n 1 "$file" | grep -q '^__ERROR__'; then
  echo "az: simulated failure" >&2
  exit 1
fi
cat "$file"
STUB
chmod +x "$work/bin/az"

failures=0
pass() { echo "ok   - $1"; }
fail() { echo "FAIL - $1"; failures=$((failures + 1)); }

# scenario <dir-name>; then call app/revision helpers
new() { export STUB_DIR="$work/$1"; rm -rf "$STUB_DIR"; mkdir -p "$STUB_DIR"; }
show() { printf '%s\n%s\n%s\n' "$3" "$4" "${5:-1}" > "$STUB_DIR/$1.show.$2"; }
rev() { printf '%s\n%s\n%s\n' "$3" "$4" "${5:-1}" > "$STUB_DIR/$1.revision.$2"; }
run() {
  PATH="$work/bin:$PATH" SLEEP_COMMAND=true TIMEOUT_SECONDS=60 INTERVAL_SECONDS=10 DEGRADED_GRACE_SECONDS=30 \
    bash "$script" birrapoint-prod-rg "$@" 2>&1
}
healthy_all() {
  for app in kc api web; do show "$app" 1 "$app--r1" "$app--r1" 1; rev "$app" 1 Running Healthy 1; done
}

new healthy; healthy_all
out="$(run kc api web)"; code=$?
[ "$code" -eq 0 ] && pass "all apps ready and healthy" || fail "healthy: exit $code: $out"

new late; healthy_all
show api 1 api--r2 api--r1; show api 2 api--r2 api--r1; show api 3 api--r2 api--r2; rev api 1 Running Healthy 1
out="$(run kc api web)"; code=$?
[ "$code" -eq 0 ] && pass "waits for the latest revision to become ready" || fail "late: exit $code: $out"

new never; healthy_all
show api 1 api--r2 api--r1
out="$(run kc api web)"; code=$?
{ [ "$code" -ne 0 ] && echo "$out" | grep -q "api" && echo "$out" | grep -qi "not ready"; } && pass "fails when the latest revision never becomes ready" || fail "never: exit $code: $out"

new failed; healthy_all
rev api 1 Failed Unhealthy 1
out="$(run kc api web)"; code=$?
{ [ "$code" -ne 0 ] && echo "$out" | grep -q "Failed"; } && pass "fails on a Failed revision" || fail "failed: exit $code: $out"

new blip; healthy_all
rev api 1 Degraded Unhealthy 1; rev api 2 Running Healthy 1
out="$(run kc api web)"; code=$?
[ "$code" -eq 0 ] && pass "tolerates a short Degraded blip" || fail "blip: exit $code: $out"

new degraded; healthy_all
rev api 1 Degraded Unhealthy 1
out="$(run kc api web)"; code=$?
{ [ "$code" -ne 0 ] && echo "$out" | grep -q "Degraded"; } && pass "fails when Degraded persists" || fail "degraded: exit $code: $out"

new unhealthy; healthy_all
rev kc 1 Running Unhealthy 1
out="$(run kc api web)"; code=$?
{ [ "$code" -ne 0 ] && echo "$out" | grep -q "kc"; } && pass "fails when the revision never turns Healthy" || fail "unhealthy: exit $code: $out"

new scale0; healthy_all
show web 1 web--r1 web--r1 0; rev web 1 Running None 0
out="$(run kc api web)"; code=$?
[ "$code" -eq 0 ] && pass "accepts a scaled-to-zero app with minReplicas 0" || fail "scale0: exit $code: $out"

new zero-but-min1; healthy_all
show web 1 web--r1 web--r1 1; rev web 1 Running None 0
out="$(run kc api web)"; code=$?
[ "$code" -ne 0 ] && pass "rejects 0 replicas when minReplicas is 1" || fail "zero-but-min1: exit $code: $out"

err() { echo "__ERROR__" > "$STUB_DIR/$1.$2.$3"; }

new az-errors; healthy_all
err api show 1; err api show 2; err api show 3; show api 4 api--r1 api--r1 1
out="$(run kc api web)"; code=$?
{ [ "$code" -ne 0 ] && echo "$out" | grep -q "simulated failure"; } && pass "fails fast after 3 consecutive az errors" || fail "az-errors: exit $code: $out"

new az-errors-revision; healthy_all
err kc revision 1; err kc revision 2; err kc revision 3; rev kc 4 Running Healthy 1
out="$(run kc api web)"; code=$?
[ "$code" -ne 0 ] && pass "counts az errors of the revision call too" || fail "az-errors-revision: exit $code: $out"

new az-blip; healthy_all
err api show 1; err api show 2; show api 3 api--r1 api--r1 1
out="$(run kc api web)"; code=$?
[ "$code" -eq 0 ] && pass "tolerates two az errors followed by success" || fail "az-blip: exit $code: $out"

[ "$failures" -eq 0 ] && echo "all tests passed" || { echo "$failures test(s) failed"; exit 1; }
