#!/usr/bin/env bash
# Prints the size label for a pull request from its changed lines (additions + deletions; pure
# renames count 0). Usage: bash .github/scripts/pr-size.sh <changed-lines>. Run by pr.yml.
set -uo pipefail

lines="${1:-}"
if ! [[ "$lines" =~ ^[0-9]+$ ]]; then
  echo "usage: pr-size.sh <changed-lines (non-negative integer)>" >&2
  exit 2
fi

if   [ "$lines" -lt 10 ];   then echo size/XS
elif [ "$lines" -lt 100 ];  then echo size/S
elif [ "$lines" -lt 400 ];  then echo size/M
elif [ "$lines" -lt 1000 ]; then echo size/L
else echo size/XL
fi
