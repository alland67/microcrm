#!/usr/bin/env bash
# SessionStart: print a short harness status so the main session knows where things stand.
set -uo pipefail
source "$(dirname "$0")/_lib.sh"
cd "$ROOT" || exit 0
echo "## Harness status"
[ -z "${TEST_CMD:-}" ] && echo "- WARNING: TEST_CMD is empty in .claude/harness.env. Run /onboard or fill it in before /build."
shopt -s nullglob
for d in specs/[0-9][0-9][0-9]-*/; do
  s="$d/spec.md"; [ -f "$s" ] || continue
  status="$(grep -m1 -iE '^\*\*Status:\*\*|^Status:' "$s" | sed -E 's/.*Status:\**[[:space:]]*//')"
  total=0; done_n=0
  if [ -f "$d/tasks.md" ]; then
    total=$(grep -cE '^[[:space:]]*- \[[ xX~]\] T-' "$d/tasks.md")
    done_n=$(grep -cE '^[[:space:]]*- \[[xX]\] T-' "$d/tasks.md")
  fi
  echo "- ${d%/} : ${status:-unknown}${total:+ ($done_n/$total tasks)}"
done
exit 0
