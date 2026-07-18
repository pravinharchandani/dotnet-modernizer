using System.ComponentModel;
using DotnetModernizer.McpServer.Models;
using DotnetModernizer.McpServer.Services;
using ModelContextProtocol.Server;

namespace DotnetModernizer.McpServer.Tools;

[McpServerToolType]
public static class EstimateMigrationEffortTool
{
    [McpServerTool(Name = "estimate_migration_effort", UseStructuredContent = true)]
    [Description("Runs the structure scan, breaking-API analysis, and NuGet compatibility " +
                 "analysis over every project of a solution and produces a .NET 10 migration " +
                 "effort estimate: per-project score, tier (quick-win/moderate/complex), the " +
                 "top score drivers, a solution-level rollup, and a suggested migration order " +
                 "(dependencies first, quick-wins first within each dependency level). " +
                 "Never throws: errors are reported in the result's error fields.")]
    public static MigrationEffortResult EstimateMigrationEffort(
        [Description("Absolute path to a .sln file")] string solutionPath)
    {
        return MigrationEffortEstimator.Estimate(solutionPath);
    }
}
