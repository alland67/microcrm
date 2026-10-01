#!/usr/bin/env bash
# Format a single file based on its type. Used by the PostToolUse hook (FORMAT_CMD).
# Never fails loudly: formatting is best-effort.
set -uo pipefail
f="${1:?file}"
root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
abs="$(cd "$(dirname "$f")" 2>/dev/null && pwd)/$(basename "$f")"
rel="${abs#"$root"/}"

case "$rel" in
  *.cs)
    dotnet format whitespace "$root" --folder --include "$rel" >/dev/null 2>&1 || true ;;
  web/*.ts|web/*.tsx|web/*.js|web/*.jsx|web/*.json|web/*.css|web/*.md|web/*.html)
    ( cd "$root/web" && npx --no-install prettier --write --log-level silent "$abs" ) >/dev/null 2>&1 || true ;;
esac
exit 0
