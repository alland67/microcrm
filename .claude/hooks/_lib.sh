#!/usr/bin/env bash
# Shared helpers for harness hooks.
ROOT="${CLAUDE_PROJECT_DIR:-$(pwd)}"
# shellcheck disable=SC1091
[ -f "$ROOT/.claude/harness.env" ] && source "$ROOT/.claude/harness.env"

# json_get <json> <jq-path> : prints value or empty; uses jq, falls back to python3
json_get() {
  local json="$1" path="$2"
  if command -v jq >/dev/null 2>&1; then
    printf '%s' "$json" | jq -r "$path // empty" 2>/dev/null
  elif command -v python3 >/dev/null 2>&1; then
    printf '%s' "$json" | python3 -c '
import json,sys
d=json.load(sys.stdin); p=sys.argv[1].lstrip(".").split(".")
for k in p:
    d=d.get(k) if isinstance(d,dict) else None
print("" if d is None else (str(d).lower() if isinstance(d,bool) else d))' "$path" 2>/dev/null
  fi
}

# rel_path <abs-or-rel path> : path relative to repo root
rel_path() {
  local p="$1"
  p="${p#"$ROOT"/}"
  p="${p#./}"
  printf '%s' "$p"
}

# matches_any <path> <space-separated globs> : 0 if any glob matches
matches_any() {
  local path="$1" globs="$2" g
  set -f
  for g in $globs; do
    # shellcheck disable=SC2053
    if [[ "$path" == $g ]]; then set +f; return 0; fi
  done
  set +f
  return 1
}
