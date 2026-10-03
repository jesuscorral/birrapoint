#!/usr/bin/env bash
# Revision health gate of the deploy pipeline (T143).
# Usage: wait-revisions.sh <resource-group> <container-app>...
# Polls every app until its latest revision is the latest READY revision and is healthy:
#   - latestRevisionName == latestReadyRevisionName;
#   - runningState is not Failed (immediate failure) and Degraded for at most DEGRADED_GRACE_SECONDS;
#   - healthState is Healthy, or the app has minReplicas 0 and no replicas (scaled to zero).
# Fails with a clear message when an app is not healthy within TIMEOUT_SECONDS.
# Needs the Azure CLI (`az`, logged in; AZURE_EXTENSION_USE_DYNAMIC_INSTALL=yes_without_prompt).
# Tests: bash .github/scripts/wait-revisions.test.sh (time is virtual there: SLEEP_COMMAND=true).
set -uo pipefail

timeout="${TIMEOUT_SECONDS:-600}"
interval="${INTERVAL_SECONDS:-10}"
grace="${DEGRADED_GRACE_SECONDS:-60}"
sleeper="${SLEEP_COMMAND:-sleep}"

if [ "$#" -lt 2 ]; then
  echo "usage: $0 <resource-group> <container-app>..." >&2
  exit 2
fi
group="$1"
shift

# Prints "ok", "pending: <reason>" or "fail: <reason>". $2 is the seconds the app has been Degraded.
check_app() {
  local app="$1" degraded_for="$2" show latest ready min detail state health replicas
  if ! show="$(az containerapp show --name "$app" --resource-group "$group" --output tsv \
    --query '[properties.latestRevisionName, properties.latestReadyRevisionName, properties.template.scale.minReplicas]' 2>&1)"; then
    echo "pending: could not read the app (${show//$'\n'/ })"
    return
  fi
  latest="$(echo "$show" | sed -n 1p)"
  ready="$(echo "$show" | sed -n 2p)"
  min="$(echo "$show" | sed -n 3p)"
  if [ -z "$latest" ]; then
    echo "pending: no revision yet"
    return
  fi
  if [ "$latest" != "$ready" ]; then
    echo "pending: latest revision $latest is not ready (latest ready: ${ready:-none})"
    return
  fi
  if ! detail="$(az containerapp revision show --name "$app" --resource-group "$group" --revision "$latest" --output tsv \
    --query '[properties.runningState, properties.healthState, properties.replicas]' 2>&1)"; then
    echo "pending: could not read revision $latest (${detail//$'\n'/ })"
    return
  fi
  state="$(echo "$detail" | sed -n 1p)"
  health="$(echo "$detail" | sed -n 2p)"
  replicas="$(echo "$detail" | sed -n 3p)"
  case "$state" in
    Failed)
      echo "fail: revision $latest runningState is Failed (healthState ${health:-unknown})"
      return
      ;;
    Degraded)
      if [ "$((degraded_for + interval))" -ge "$grace" ]; then
        echo "fail: revision $latest has been Degraded for ${grace}s"
      else
        echo "pending: revision $latest is Degraded"
      fi
      return
      ;;
  esac
  if [ "${min:-1}" = "0" ] && [ "${replicas:-0}" = "0" ]; then
    echo "ok"
    return
  fi
  if [ "$health" = "Healthy" ]; then
    echo "ok"
    return
  fi
  echo "pending: revision $latest runningState ${state:-unknown}, healthState ${health:-unknown}"
}

declare -A done_apps degraded_secs last_reason
for app in "$@"; do degraded_secs[$app]=0; done

elapsed=0
while true; do
  remaining=0
  for app in "$@"; do
    [ -n "${done_apps[$app]:-}" ] && continue
    result="$(check_app "$app" "${degraded_secs[$app]}")"
    case "$result" in
      ok)
        done_apps[$app]=1
        echo "$app: healthy"
        ;;
      fail:*)
        echo "::error::$app: ${result#fail: }" >&2
        echo "$app: ${result#fail: }" >&2
        exit 1
        ;;
      *)
        remaining=$((remaining + 1))
        last_reason[$app]="${result#pending: }"
        case "${result#pending: }" in
          *Degraded*) degraded_secs[$app]=$((degraded_secs[$app] + interval)) ;;
          *) degraded_secs[$app]=0 ;;
        esac
        ;;
    esac
  done
  [ "$remaining" -eq 0 ] && exit 0
  if [ "$elapsed" -ge "$timeout" ]; then
    for app in "$@"; do
      [ -n "${done_apps[$app]:-}" ] && continue
      echo "::error::$app is not ready after ${timeout}s: ${last_reason[$app]}" >&2
      echo "$app is not ready after ${timeout}s: ${last_reason[$app]}" >&2
    done
    exit 1
  fi
  "$sleeper" "$interval"
  elapsed=$((elapsed + interval))
done
