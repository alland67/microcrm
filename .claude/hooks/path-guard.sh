#!/usr/bin/env bash
# PreToolUse guard for Edit/Write/MultiEdit/NotebookEdit.
# Usage: path-guard.sh allow <VAR>   -> only paths matching globs in $VAR may be written
#        path-guard.sh deny  <VAR>   -> paths matching globs in $VAR may NOT be written
# Exit 2 blocks the tool call and feeds stderr back to the agent.
set -uo pipefail
source "$(dirname "$0")/_lib.sh"

MODE="${1:-deny}"; VAR="${2:-PROTECTED_GLOBS}"
INPUT="$(cat)"
FILE="$(json_get "$INPUT" '.tool_input.file_path')"
[ -z "$FILE" ] && FILE="$(json_get "$INPUT" '.tool_input.notebook_path')"
[ -z "$FILE" ] && exit 0

REL="$(rel_path "$FILE")"
GLOBS="${!VAR:-}"
AGENT="$(json_get "$INPUT" '.agent_type')"; AGENT="${AGENT:-this agent}"

if [ "$MODE" = "allow" ]; then
  if ! matches_any "$REL" "$GLOBS"; then
    echo "BLOCKED by harness: $AGENT may only write files matching $VAR ($GLOBS). Refused: $REL. If this change is truly needed, stop and report it as a follow-up for the orchestrator." >&2
    exit 2
  fi
else
  if matches_any "$REL" "$GLOBS"; then
    echo "BLOCKED by harness: $REL is protected for $AGENT ($VAR). Do not work around this; report it in your final report instead." >&2
    exit 2
  fi
fi
exit 0
