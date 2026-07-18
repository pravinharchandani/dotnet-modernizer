namespace DotnetModernizer.McpServer.Models;

/// <summary>
/// Top-level result of estimate_migration_effort. <see cref="Error"/> is non-null only
/// for solution-level failures (missing/unreadable sln); per-project analysis failures
/// are reported on the individual <see cref="ProjectEffortEstimate.Error"/> instead.
/// </summary>
public sealed record MigrationEffortResult(
    string SolutionPath,
    IReadOnlyList<ProjectEffortEstimate> Projects,
    SolutionEffortSummary Summary,
    IReadOnlyList<string> SuggestedMigrationOrder,
    string? Error = null);

/// <summary>
/// Effort estimate for one project. <see cref="Tier"/> is "quick-win", "moderate",
/// "complex", or "unknown" when the project could not be analyzed (see <see cref="Error"/>).
/// </summary>
public sealed record ProjectEffortEstimate(
    string ProjectName,
    string ProjectKind,
    double Score,
    string Tier,
    IReadOnlyList<EffortDriver> TopDrivers,
    string? Error = null);

/// <summary>One named contributor to a project's score, e.g. "3 incompatible NuGet packages".</summary>
public sealed record EffortDriver(string Name, double Points);

public sealed record SolutionEffortSummary(
    double TotalScore,
    int QuickWinCount,
    int ModerateCount,
    int ComplexCount);
