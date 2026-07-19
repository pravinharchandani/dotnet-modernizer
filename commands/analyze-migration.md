---
description: Run a full .NET 10 migration assessment on a legacy .NET Framework solution
argument-hint: <path-to-solution.sln>
---

Run a migration assessment for the solution at: $ARGUMENTS

If no path was given, ask the user for the path to the `.sln` file (or project directory) before doing anything else.

Delegate the entire assessment to the `migration-analyst` subagent (listed as `dotnet-modernizer:migration-analyst` when this plugin is installed). Pass it the solution path and instruct it to produce its full structured Migration Assessment report: executive summary, migration order table, per-project detail (breaking APIs with file:line references, NuGet package actions), and decision points.

Do not run the analysis tools yourself in the main thread — the subagent owns the workflow. When the subagent returns, relay its complete report to the user.
