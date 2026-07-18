namespace DotnetModernizer.McpServer.Models;

/// <summary>
/// Top-level result of find_breaking_apis. <see cref="Error"/> is non-null only for
/// tool-level failures (missing project file, malformed csproj, invalid filter);
/// an analyzable project with no incompatible API usage returns an empty findings list.
/// </summary>
public sealed record BreakingApiScanResult(
    string ProjectPath,
    IReadOnlyList<BreakingApiFinding> Findings,
    BreakingApiSummary Summary,
    string? Error = null);

/// <summary>One use of an API that blocks or complicates migration to .NET 10.</summary>
/// <param name="RuleFamily">
/// FullName of the catalog rule that matched. Groups many findings into one migration
/// problem: 40 uses of System.Web.Mvc types are all family "System.Web".
/// </param>
public sealed record BreakingApiFinding(
    string FilePath,
    int Line,
    string ApiName,
    string Severity,
    string Recommendation,
    string DetectionLevel,
    string RuleFamily);

public sealed record BreakingApiSummary(
    int FilesScanned,
    int BlockerCount,
    int WarningCount);
