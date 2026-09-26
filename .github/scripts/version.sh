#!/usr/bin/env bash
# Release version helpers for release.yml (T135, FR-064). version.txt at the repository root holds
# the version the NEXT release will publish; the release pipeline reads it from main and, once
# the release exists, advances it.
#
#   version.sh read <file>            print the version in <file> (whitespace/CRLF trimmed)
#   version.sh validate <version>     print <version> if it is X.Y.Z, fail otherwise
#   version.sh next <version> <bump>  print the version after <version>; bump = patch|minor|major
#
# Tests: bash .github/scripts/version.test.sh
set -euo pipefail

semver_re='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'

fail() {
  echo "version.sh: $*" >&2
  exit 1
}

validate() {
  [[ "${1:-}" =~ $semver_re ]] || fail "'${1:-}' is not a release version X.Y.Z"
  printf '%s\n' "$1"
}

case "${1:-}" in
  read)
    [ -f "${2:-}" ] || fail "file '${2:-}' not found"
    validate "$(tr -d '[:space:]' < "$2")"
    ;;
  validate)
    validate "${2:-}"
    ;;
  next)
    validate "${2:-}" > /dev/null
    IFS=. read -r major minor patch <<< "$2"
    case "${3:-}" in
      patch) patch=$((patch + 1)) ;;
      minor) minor=$((minor + 1)); patch=0 ;;
      major) major=$((major + 1)); minor=0; patch=0 ;;
      *) fail "unknown bump '${3:-}' (expected patch, minor or major)" ;;
    esac
    printf '%s.%s.%s\n' "$major" "$minor" "$patch"
    ;;
  *)
    fail "usage: version.sh read <file> | validate <version> | next <version> <patch|minor|major>"
    ;;
esac
