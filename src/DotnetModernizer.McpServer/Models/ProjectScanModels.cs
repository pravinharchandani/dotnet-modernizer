namespace DotnetModernizer.McpServer.Models;

/// <summary>
/// Top-level result of scan_project_structure. <see cref="Error"/> is non-null only
/// for solution-level failures (missing file, unreadable sln); per-project failures
/// are reported on the individual <see cref="ProjectInfo.Error"/> instead.
/// </summary>
public sealed record ProjectScanResult(
    string SolutionPath,
    IReadOnlyList<ProjectInfo> Projects,
    SolutionSummary SolutionSummary,
    string? Error = null);

public sealed record ProjectInfo(
    string ProjectName,
    string RelativePath,
    string ProjectStyle,
    string? TargetFramework,
    string ProjectKind,
    IReadOnlyList<string> ProjectReferences,
    string? Error = null);

public sealed record SolutionSummary(
    int TotalProjects,
    int LegacyStyleCount,
    string? LowestFramework);
