#!/usr/bin/env bash
# Stop hook (opt-in via STOP_GATE=1): if there are uncommitted changes and the
# test suite fails, block Claude from ending its turn and show the failures.
set -uo pipefail
source "$(dirname "$0")/_lib.sh"
[ "${STOP_GATE:-0}" != "1" ] && exit 0
[ -z "${TEST_CMD:-}" ] && exit 0
INPUT="$(cat)"
# Avoid infinite loops: if we already blocked once this turn, let it stop.
[ "$(json_get "$INPUT" '.stop_hook_active')" = "true" ] && exit 0
cd "$ROOT" || exit 0
git rev-parse --is-inside-work-tree >/dev/null 2>&1 || exit 0
[ -z "$(git status --porcelain 2>/dev/null)" ] && exit 0

OUT="$(eval "$TEST_CMD" 2>&1)"; CODE=$?
if [ $CODE -ne 0 ]; then
  {
    echo "STOP GATE: uncommitted changes exist and '$TEST_CMD' is failing (exit $CODE)."
    echo "Either fix the failures or explain explicitly why the suite is expected to be red (e.g. mid-TDD RED step)."
    echo "--- last 40 lines ---"
    printf '%s\n' "$OUT" | tail -n 40
  } >&2
  exit 2
fi
exit 0
