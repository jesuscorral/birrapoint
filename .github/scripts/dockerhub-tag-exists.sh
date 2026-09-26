#!/usr/bin/env bash
# Does <namespace>/<repository>:<tag> exist on Docker Hub? (T135 — release immutability checks)
#
#   dockerhub-tag-exists.sh <namespace> <repository> <tag>
#   exit 0 = exists, 1 = does not exist, 2 = lookup failed (never treated as "missing")
#
# Authenticates with DOCKERHUB_USERNAME + DOCKERHUB_TOKEN when both are set, so private
# repositories are seen too; otherwise anonymous.
set -euo pipefail

[ $# -eq 3 ] || { echo 'usage: dockerhub-tag-exists.sh <namespace> <repository> <tag>' >&2; exit 2; }
namespace="$1" repository="$2" tag="$3"

auth=()
if [ -n "${DOCKERHUB_USERNAME:-}" ] && [ -n "${DOCKERHUB_TOKEN:-}" ]; then
  if ! token="$(jq -n --arg u "$DOCKERHUB_USERNAME" --arg p "$DOCKERHUB_TOKEN" '{identifier: $u, secret: $p}' |
    curl -fsS --max-time 30 -X POST -H 'Content-Type: application/json' --data @- \
      https://hub.docker.com/v2/auth/token | jq -er '.access_token')"; then
    echo 'Docker Hub authentication failed.' >&2
    exit 2
  fi
  [ -n "${GITHUB_ACTIONS:-}" ] && echo "::add-mask::$token"
  auth=(-H "Authorization: Bearer $token")
fi

# ${auth[@]+...}: an empty array under `set -u` is an error on bash < 4.4.
status="$(curl -sS --max-time 30 -o /dev/null -w '%{http_code}' ${auth[@]+"${auth[@]}"} \
  "https://hub.docker.com/v2/namespaces/$namespace/repositories/$repository/tags/$tag")" || status='000'

case "$status" in
  200) exit 0 ;;
  404) exit 1 ;;
  *) echo "Docker Hub lookup of $namespace/$repository:$tag returned HTTP $status." >&2; exit 2 ;;
esac
