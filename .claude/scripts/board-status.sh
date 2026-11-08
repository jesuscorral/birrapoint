#!/usr/bin/env bash
# Moves an issue's card on the BirraPoint project board (github.com/users/jesuscorral/projects/10).
# Usage: bash .claude/scripts/board-status.sh <issue-number> "<Backlog|Ready|In progress|In review|Done>"
# Adds the issue to the board first if it is missing. Needs a gh token with the `project` scope.
set -euo pipefail

issue="${1:?issue number required}"
status="${2:?status required}"
owner=jesuscorral
number=10
project_id=PVT_kwHOAFw6AM4BqvYv
field_id=PVTSSF_lAHOAFw6AM4BqvYvzhpFHvA

case "$status" in
  "Backlog") option=f75ad846 ;;
  "Ready") option=61e4505c ;;
  "In progress") option=47fc9ee4 ;;
  "In review") option=df73e18b ;;
  "Done") option=98236657 ;;
  *) echo "unknown status: $status" >&2; exit 2 ;;
esac

url="https://github.com/$owner/birrapoint/issues/$issue"
item="$(gh project item-add "$number" --owner "$owner" --url "$url" --format json --jq .id)"
gh project item-edit --id "$item" --project-id "$project_id" --field-id "$field_id" \
  --single-select-option-id "$option" >/dev/null
echo "issue #$issue -> $status"
