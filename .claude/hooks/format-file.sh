#!/usr/bin/env bash
# PostToolUse: format the file that was just written, if FORMAT_CMD is set. Never blocks.
set -uo pipefail
source "$(dirname "$0")/_lib.sh"
[ -z "${FORMAT_CMD:-}" ] && exit 0
INPUT="$(cat)"
FILE="$(json_get "$INPUT" '.tool_input.file_path')"
[ -z "$FILE" ] || [ ! -f "$FILE" ] && exit 0
( cd "$ROOT" && eval "$FORMAT_CMD \"\$FILE\"" ) >/dev/null 2>&1 || true
exit 0
