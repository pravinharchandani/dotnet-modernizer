# dotnet-modernizer

A Claude Code plugin that assesses .NET Framework → .NET 10 migration effort in minutes instead of weeks. Point it at a legacy solution and it inventories every project, detects removed and incompatible APIs with Roslyn semantic analysis, checks every NuGet reference for .NET 10 compatibility, and produces a scored, prioritized migration backlog — then drafts the first fixes on request. The timing matters: .NET 8 and .NET 9 both leave support in November 2026, which makes .NET 10 LTS (supported through November 2028) the only sensible landing zone for a migration starting now. If you have Framework estate and no current-state assessment, this tool exists to produce one before that window forces the decision for you.

## Quickstart (60 seconds)

Prerequisites: [Claude Code](https://claude.com/claude-code), .NET 10 SDK.

```
# 1. Add this repo as a marketplace and install the plugin
/plugin marketplace add <your-github-owner>/dotnet-modernizer
/plugin install dotnet-modernizer

# 2. Run an assessment against your solution
/analyze-migration C:\src\YourLegacyApp\YourLegacyApp.sln
```

The command delegates to the `migration-analyst` subagent, which runs the four analysis tools and returns a structured Migration Assessment: executive summary, migration order table, per-project findings with `file:line` references, and the decision points that need human judgment.

To try it without a legacy solution of your own, run it against the bundled fixture:

```
/analyze-migration test-fixtures/LegacyShop
```

## Architecture

```mermaid
graph LR
    subgraph "Claude Code"
        CMD["/analyze-migration"] --> MA[migration-analyst<br/>subagent, read-only]
        USER[User request<br/>&quot;fix this finding&quot;] --> CW[codemod-writer<br/>subagent, edits code]
        H1[PreToolUse hook<br/>guard-write-path] -.blocks writes outside repo.- CW
        H2[PostToolUse hook<br/>format-cs] -.formats touched .cs files.- CW
    end
    subgraph "MCP server (C# / net10.0)"
        T1[scan_project_structure]
        T2[find_breaking_apis]
        T3[analyze_nuget_compat]
        T4[estimate_migration_effort]
    end
    MA --> T1 & T2 & T3 & T4
    T2 --> R[Roslyn<br/>semantic model]
    T3 --> N[NuGet<br/>flat-container API]
    T1 --> FS[csproj / sln<br/>parsing]
    T4 --> SW[Scoring model]
```

### MCP server: four narrow tools, not one big one

The server (`src/DotnetModernizer.McpServer`, built on the official `ModelContextProtocol` package) exposes four tools instead of a single "analyze everything" endpoint. Each tool returns a structured, JSON-serializable record — never free-form prose — so the agent composes them: scan first to enumerate projects, then per-project API and package analysis, then scoring over the combined results. Narrow tools keep each call's output small enough to reason over, let the agent skip work that doesn't apply (no NuGet analysis for a project with no package references), and make failures local — a malformed csproj fails one call, not the whole assessment.

### Breaking-API detection: semantic analysis, not regex

`find_breaking_apis` builds a Roslyn compilation and matches resolved symbols against a catalog of APIs removed or fundamentally changed in .NET 10 (System.Web, WCF contracts, BinaryFormatter, Remoting, EnterpriseServices, AppDomain.CreateDomain, Microsoft.VisualBasic). Regex over source text cannot tell `System.Web.HttpContext` the API from `"System.Web"` the string literal, misses using-aliases, and false-positives on comments. Detection is two-level: semantic matching on the full metadata name where the symbol resolves, and a syntactic fallback (qualified names and using-directives, aliases expanded) where it can't — legacy net472 code usually won't fully compile on the analysis machine because its reference assemblies are absent. Both levels operate on syntax and symbol nodes only, so strings and comments can never produce findings.

### NuGet compatibility: layered, and honest about uncertainty

`analyze_nuget_compat` decides package compatibility in layers: an offline catalog of the common legacy suspects first, then live NuGet flat-container metadata (a package whose latest version targets `netstandard2.x` or `net5.0+` is consumable from net10.0), and finally `unknown` — it never guesses when the network is unavailable and the catalog is silent.

### Subagents: context isolation with opposite tool grants

`migration-analyst` runs the full assessment in its own context window, so scanning a 40-project solution doesn't flood the main conversation with raw tool output — only the finished report comes back. It is read-only by construction: its tool allowlist is the four MCP tools plus `Read`, with no Write, Edit, or Bash. `codemod-writer` is the inverse — it gets Read/Edit/Write and `dotnet build`/`dotnet test`, but none of the analysis tools, because it receives one concrete finding as input and re-scanning the solution would waste its context on data it doesn't need. Analysis can't mutate; mutation can't wander.

### Hooks: a safety boundary the model can't talk itself out of

A `PreToolUse` hook blocks any Write/Edit that resolves outside the project root — path-normalized and traversal-resolved, so `..\..\` tricks don't work. This matters specifically for a tool whose job is editing legacy code: the codemod agent is pointed at solutions full of absolute paths and generated files, and the hook makes "edit the wrong tree" structurally impossible rather than relying on prompt instructions. A `PostToolUse` hook runs `dotnet format whitespace` on touched `.cs` files, best-effort, so formatting never depends on the model remembering to do it.

## Sample output

Trimmed from a real `migration-analyst` run against the bundled fixture (`test-fixtures/LegacyShop`, a synthetic four-project .NET Framework 4.7.2 solution):

> Solution: `test-fixtures\LegacyShop\LegacyShop.sln` — 4 projects, all legacy-style, all targeting .NET Framework 4.7.2. Total effort score: **44.5** (1 quick-win, 1 moderate, 2 complex).
>
> **Executive summary.** The LegacyShop system can be moved to the current long-term-supported .NET platform, but two of its four parts — the website and the ordering service — are built on technology that no longer exists on the new platform and will need their outer layers rebuilt rather than simply updated. The two shared libraries are much easier: one is nearly effortless and the other has a single outdated communication mechanism to replace. Recommended approach: modernize the two libraries first to build confidence, then tackle the website and the service, each of which requires a technology decision before work begins.
>
> **Migration order**
>
> | # | Project | Tier | Score | Top drivers | Key risks |
> |---|---------|------|-------|-------------|-----------|
> | 1 | LegacyShop.Utils | quick-win | 2.1 | classlib base (1); legacy csproj (1); 1 dependent (0.1) | None found — 0 breaking APIs, 0 package issues |
> | 2 | LegacyShop.Core | moderate | 6.4 | 1 blocker API family (3); classlib base (1); legacy csproj (1) | .NET Remoting removed entirely on .NET 10 — requires redesign of the order channel |
> | 3 | LegacyShop.Web | complex | 19 | web base (8); 3 incompatible NuGet packages (6); 1 blocker family (3) | System.Web / ASP.NET MVC 5 does not exist on .NET 10 — 12 blocker findings; web layer rewritten on ASP.NET Core |
> | 4 | LegacyShop.Services | complex | 17 | wcf base (10); 2 blocker families (6); legacy csproj (1) | WCF server stack not on .NET 10 — needs CoreWCF or gRPC/REST rewrite |
>
> **Per-project detail (excerpt — LegacyShop.Core)**
>
> Breaking APIs — Remoting family (all blockers, recommendation: removed; migrate to IPC/gRPC):
> - `System.Runtime.Remoting` — `RemotingOrderChannel.cs:2` (semantic detection)
> - `System.Runtime.Remoting.Channels` — `RemotingOrderChannel.cs:3`
> - `System.Runtime.Remoting.Channels.Tcp` — `RemotingOrderChannel.cs:4`
>
> Package actions:
>
> | Package | Version | Status | Action |
> |---------|---------|--------|--------|
> | System.Text.Json | 8.0.5 | compatible-with-upgrade | Upgrade to 10.0.10 |
>
> **Decision points (excerpt)**
>
> 1. **WCF replacement: CoreWCF vs gRPC/REST (LegacyShop.Services).** CoreWCF preserves the existing service/operation contracts and SOAP wire format, minimizing changes for existing callers; gRPC or REST is a cleaner long-term architecture but breaks every existing client. The deciding factor is whether you control all clients of `IOrderService` and can update them in lockstep.
> 2. **Remoting replacement: local IPC vs gRPC (LegacyShop.Core).** .NET Remoting is removed with no drop-in substitute. The finding shows a TCP channel, so if it crosses machines, gRPC is the appropriate replacement; if it only crosses process boundaries on one machine, named-pipe IPC is the smaller change.
> 3. **Data access: stay on EF6 (6.3+) vs move to EF Core 10 (LegacyShop.Web).** EF6 6.3+ runs on .NET 10, making it the fast path; EF Core migration touches query and model code, so decide whether to fold it in or defer.

The full report also includes per-project findings for the web and WCF projects (12 System.Web-family blockers with `file:line` references, incompatible `Microsoft.AspNet.*` package removals) and two further decision points on web rewrite scope and serializer consolidation.

## Scoring model

`estimate_migration_effort` scores each project and assigns a tier: **quick-win** (≤ 4), **moderate** (≤ 12), **complex** (above 12).

| Factor | Points | Rationale |
|---|---|---|
| Base: class library / test | 1 | Mostly a port: retarget the TFM, fix API fallout |
| Base: console app | 2 | Port plus entry-point and config plumbing |
| Base: web (System.Web) | 8 | Hosting-model rewrite: ASP.NET Core changes routing, pipeline, config, startup even when business code moves unchanged |
| Base: WCF service | 10 | Above web because the contract/binding model has no first-party successor (CoreWCF or gRPC, both real migrations) |
| Per blocker API family | +3 | One catalog rule is one problem to solve, however many call sites — 40 usages of System.Web.Mvc are one problem, not forty |
| Per warning API family | +1 | Known mechanical replacement exists (e.g. AssemblyLoadContext), a third of a blocker |
| Per incompatible package | +2 | Forces finding and adopting a replacement library — open-ended |
| Per upgrade-needed package | +0.5 | Version bump plus fixing what the newer major broke |
| Dependency factor | ×(1 + 0.1 per dependent) | Churn in a widely-referenced project ripples into every dependent's build and tests |
| Legacy-style csproj | +1 flat | SDK-style conversion is real but bounded, and doesn't grow with reference count |

The weights live in one file (`src/DotnetModernizer.McpServer/Services/ScoringWeights.cs`) as pure data, so the model can be tuned without touching estimation code. Call-site counts deliberately don't multiply into the score: the number of usages is typing effort, which the base score already covers; the number of distinct problem *families* is what predicts calendar time.

## Repository layout

The repository root is the plugin root — the layout follows the Claude Code plugin structure.

```
.claude-plugin/plugin.json        Plugin manifest (wires MCP server, agents, hooks)
.claude-plugin/marketplace.json   Marketplace catalog (install directly from this repo)
src/DotnetModernizer.McpServer/   C# MCP server (net10.0)
test-fixtures/LegacyShop/         Synthetic legacy .NET Framework solution for testing
agents/                           migration-analyst, codemod-writer
commands/                         /analyze-migration
hooks/                            hooks.json, guard-write-path.sh, format-cs.sh
```

## Roadmap

- **Config-file transforms** — `web.config` → `appsettings.json` + `Program.cs` equivalents (connection strings, app settings, HTTP modules to middleware), as a codemod the writer agent can apply.
- **Solution-wide codemod batches** — apply one finding family (e.g. every BinaryFormatter usage) across the whole solution in a single reviewed batch, instead of one file per request.
- **CI mode** — run the analysis tools headless in a pipeline and fail the build when new Framework-only API usage is introduced, so a migration in progress can't regress.

## License

MIT
