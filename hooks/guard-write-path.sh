#!/bin/sh
# PreToolUse hook (matcher: Write|Edit) for dotnet-modernizer.
# Reads the hook payload JSON from stdin, extracts tool_input.file_path, and
# blocks any write that resolves outside the project root.
#
# Exit code contract (per Claude Code hooks docs):
#   0 - allow: permission flow continues normally
#   2 - block: the tool call is prevented and stderr is fed back to Claude
#       as feedback, so the message below is what Claude sees.

payload=$(cat)

# Prefer jq; fall back to sed extraction (first "file_path" value) when jq
# is not installed. The sed path leaves JSON backslash escapes (\\) in the
# value; normalization below converts them to forward slashes anyway.
if command -v jq >/dev/null 2>&1; then
    file_path=$(printf '%s' "$payload" | jq -r '.tool_input.file_path // empty')
else
    file_path=$(printf '%s' "$payload" \
        | sed -n 's/.*"file_path"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
        | head -n 1)
fi

# No file path in payload: nothing to guard.
[ -n "$file_path" ] || exit 0

# Normalize a path for comparison: backslashes -> slashes, collapse repeats,
# convert to the shell's native form (cygpath on Git Bash), resolve ../
# traversal without requiring the file to exist (realpath -m), lowercase
# because Windows paths are case-insensitive.
normalize() {
    p=$(printf '%s' "$1" | tr '\\' '/' | tr -s '/')
    if command -v cygpath >/dev/null 2>&1; then
        p=$(cygpath -u "$p" 2>/dev/null) || p=$(printf '%s' "$1" | tr '\\' '/' | tr -s '/')
    fi
    if command -v realpath >/dev/null 2>&1; then
        p=$(realpath -m "$p" 2>/dev/null) || :
    fi
    printf '%s' "$p" | tr '[:upper:]' '[:lower:]'
}

project_root=$(normalize "${CLAUDE_PROJECT_DIR:-$PWD}")
target=$(normalize "$file_path")

case "$target" in
    "$project_root" | "$project_root"/*)
        exit 0
        ;;
esac

echo "dotnet-modernizer: blocked write outside project root: $file_path" >&2
exit 2
