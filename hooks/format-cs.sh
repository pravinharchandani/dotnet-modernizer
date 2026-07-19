#!/bin/sh
# PostToolUse hook (matcher: Edit|Write) for dotnet-modernizer.
# If the touched file is a .cs file and dotnet is available, run whitespace
# formatting on it. Best-effort only: formatting must never fail the
# pipeline, so this script always exits 0.

payload=$(cat)

if command -v jq >/dev/null 2>&1; then
    file_path=$(printf '%s' "$payload" | jq -r '.tool_input.file_path // empty')
else
    file_path=$(printf '%s' "$payload" \
        | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        | head -n 1)
fi

case "$file_path" in
    *.cs) ;;
    *) exit 0 ;;
esac

command -v dotnet >/dev/null 2>&1 || exit 0

dotnet format whitespace --include "$file_path" --folder >/dev/null 2>&1 || true
exit 0
