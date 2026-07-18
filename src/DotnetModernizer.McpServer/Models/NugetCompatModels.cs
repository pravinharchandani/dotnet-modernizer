namespace DotnetModernizer.McpServer.Models;

/// <summary>
/// Top-level result of analyze_nuget_compat. <see cref="Error"/> is non-null only for
/// tool-level failures (missing project file, malformed csproj or packages.config);
/// a project that references no packages returns an empty package list.
/// </summary>
public sealed record NugetCompatResult(
    string ProjectPath,
    IReadOnlyList<PackageCompatibility> Packages,
    string? Error = null);

/// <summary>.NET 10 compatibility verdict for one referenced NuGet package.</summary>
public sealed record PackageCompatibility(
    string PackageId,
    string CurrentVersion,
    string Status,
    string RecommendedAction,
    string? ReplacementPackage);
