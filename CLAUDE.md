# dotnet-modernizer

Claude Code plugin that analyzes legacy .NET Framework solutions and estimates migration effort to .NET 10 (current LTS, supported through November 2028).

## Project conventions

- **Language/runtime:** C# / .NET 10 for the MCP server (`net10.0` TFM), using the official `ModelContextProtocol` NuGet package.
- **Migration target:** All analysis and recommendations target .NET 10 (`net10.0`).
- **Roslyn analysis:** Always use semantic model APIs. Never regex over source text.
- **Tool output:** MCP tools return structured JSON-serializable records, never free-form strings.
- **Error handling:** Every tool must handle: path not found, malformed csproj, empty solution.
- **Build discipline:** Run `dotnet build` after every change to `src/` and fix errors before finishing.
- **File layout:** Keep each tool class in its own file under `src/DotnetModernizer.McpServer/Tools/`.

## Repository layout

The repository root is also the plugin root — the layout follows the Claude Code plugin structure.

```
.claude-plugin/plugin.json        Plugin manifest (wires MCP server, agents, hooks)
.claude-plugin/marketplace.json   Marketplace catalog (lets users install from this repo)
src/DotnetModernizer.McpServer/   C# MCP server
test-fixtures/LegacyShop/         Synthetic legacy .NET Framework solution for testing
agents/                           Subagent definitions
commands/                         Slash commands (/analyze-migration)
hooks/                            Hook config (hooks.json) and hook scripts
```

## Plugin notes

- The MCP server has two scoped tool-name prefixes: `mcp__dotnet-modernizer__*` in dev (project `.mcp.json`), `mcp__plugin_dotnet-modernizer_analyzer__*` when installed as a plugin. Agent `tools:` allowlists must list both.
- `.claude/settings.json` and `.claude/agents/` mirror the plugin hooks/agents for development in this repo before packaging; keep them in sync with `hooks/` and `agents/`.
