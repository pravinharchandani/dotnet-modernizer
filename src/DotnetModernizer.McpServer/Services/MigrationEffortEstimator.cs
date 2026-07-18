using DotnetModernizer.McpServer.Analysis;
using DotnetModernizer.McpServer.Models;
using DotnetModernizer.McpServer.Scanning;

namespace DotnetModernizer.McpServer.Services;

/// <summary>
/// Orchestrates the three per-project analyses (structure scan, breaking-API scan,
/// NuGet compatibility) into a per-project effort score and a solution-level rollup.
/// Weights live in <see cref="ScoringWeights"/>; this class only combines results.
/// </summary>
public static class MigrationEffortEstimator
{
    public const string TierQuickWin = "quick-win";
    public const string TierModerate = "moderate";
    public const string TierComplex = "complex";
    public const string TierUnknown = "unknown";

    public static MigrationEffortResult Estimate(string solutionPath, INuGetMetadataClient? nugetClient = null)
    {
        ProjectScanResult scan = SolutionScanner.Scan(solutionPath);
        if (scan.Error is not null)
        {
            return new MigrationEffortResult(solutionPath ?? string.Empty, [],
                new SolutionEffortSummary(0, 0, 0, 0), [], scan.Error);
        }

        string solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;

        // Dependents per project name: how many other projects reference it.
        Dictionary<string, int> dependentCounts = scan.Projects.ToDictionary(
            p => p.ProjectName,
            p => scan.Projects.Count(other =>
                other.ProjectName != p.ProjectName &&
                other.ProjectReferences.Contains(p.ProjectName, StringComparer.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);

        var estimates = scan.Projects
            .Select(p => EstimateProject(p, solutionDir, dependentCounts[p.ProjectName], nugetClient))
            .ToList();

        var summary = new SolutionEffortSummary(
            Math.Round(estimates.Sum(e => e.Score), 2),
            estimates.Count(e => e.Tier == TierQuickWin),
            estimates.Count(e => e.Tier == TierModerate),
            estimates.Count(e => e.Tier == TierComplex));

        return new MigrationEffortResult(solutionPath!, estimates, summary, BuildMigrationOrder(scan, estimates));
    }

    private static ProjectEffortEstimate EstimateProject(
        ProjectInfo project, string solutionDir, int dependents, INuGetMetadataClient? nugetClient)
    {
        if (project.Error is not null)
        {
            return new ProjectEffortEstimate(project.ProjectName, project.ProjectKind, 0, TierUnknown, [], project.Error);
        }

        string csprojPath = Path.GetFullPath(Path.Combine(
            solutionDir, project.RelativePath.Replace('\\', Path.DirectorySeparatorChar)));

        BreakingApiScanResult apis = BreakingApiAnalyzer.Analyze(csprojPath);
        if (apis.Error is not null)
        {
            return new ProjectEffortEstimate(project.ProjectName, project.ProjectKind, 0, TierUnknown, [], apis.Error);
        }

        NugetCompatResult packages = NugetCompatAnalyzer.Analyze(csprojPath, nugetClient);
        if (packages.Error is not null)
        {
            return new ProjectEffortEstimate(project.ProjectName, project.ProjectKind, 0, TierUnknown, [], packages.Error);
        }

        int blockerFamilies = CountFamilies(apis, BreakingApiCatalog.SeverityBlocker);
        int warningFamilies = CountFamilies(apis, BreakingApiCatalog.SeverityWarning);
        int incompatiblePackages = packages.Packages.Count(p => p.Status == NugetCompatAnalyzer.Incompatible);
        int upgradePackages = packages.Packages.Count(p => p.Status == NugetCompatAnalyzer.CompatibleWithUpgrade);
        bool isLegacyStyle = project.ProjectStyle == "legacy";

        double baseScore = ScoringWeights.BaseFor(project.ProjectKind);
        double apiPoints = blockerFamilies * ScoringWeights.BlockerFamilyPoints +
                           warningFamilies * ScoringWeights.WarningFamilyPoints;
        double packagePoints = incompatiblePackages * ScoringWeights.IncompatiblePackagePoints +
                               upgradePackages * ScoringWeights.UpgradePackagePoints;

        // The dependency factor multiplies the analysis-driven work; the SDK-style
        // conversion is flat because it does not grow with the number of dependents.
        double subtotal = baseScore + apiPoints + packagePoints;
        double dependencyFactor = 1 + ScoringWeights.DependentFactorStep * dependents;
        double legacyPoints = isLegacyStyle ? ScoringWeights.LegacyCsprojPoints : 0;
        double score = Math.Round(subtotal * dependencyFactor + legacyPoints, 2);

        var drivers = new List<EffortDriver>
        {
            new($"{project.ProjectKind} project base effort", baseScore),
        };
        if (blockerFamilies > 0)
        {
            drivers.Add(new($"{blockerFamilies} blocker API {Pluralize("family", "families", blockerFamilies)}",
                blockerFamilies * ScoringWeights.BlockerFamilyPoints));
        }
        if (warningFamilies > 0)
        {
            drivers.Add(new($"{warningFamilies} warning API {Pluralize("family", "families", warningFamilies)}",
                warningFamilies * ScoringWeights.WarningFamilyPoints));
        }
        if (incompatiblePackages > 0)
        {
            drivers.Add(new($"{incompatiblePackages} incompatible NuGet {Pluralize("package", "packages", incompatiblePackages)}",
                incompatiblePackages * ScoringWeights.IncompatiblePackagePoints));
        }
        if (upgradePackages > 0)
        {
            drivers.Add(new($"{upgradePackages} NuGet {Pluralize("package", "packages", upgradePackages)} needing upgrade",
                upgradePackages * ScoringWeights.UpgradePackagePoints));
        }
        if (dependents > 0)
        {
            drivers.Add(new($"referenced by {dependents} {Pluralize("project", "projects", dependents)}",
                Math.Round(subtotal * (dependencyFactor - 1), 2)));
        }
        if (isLegacyStyle)
        {
            drivers.Add(new("legacy-style csproj conversion", legacyPoints));
        }

        var topDrivers = drivers
            .OrderByDescending(d => d.Points)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        return new ProjectEffortEstimate(project.ProjectName, project.ProjectKind, score, TierOf(score), topDrivers);
    }

    /// <summary>Distinct catalog rules of the given severity with at least one finding.</summary>
    private static int CountFamilies(BreakingApiScanResult apis, string severity) =>
        apis.Findings
            .Where(f => f.Severity == severity)
            .Select(f => f.RuleFamily)
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static string TierOf(double score) => score switch
    {
        <= ScoringWeights.QuickWinMaxScore => TierQuickWin,
        <= ScoringWeights.ModerateMaxScore => TierModerate,
        _ => TierComplex,
    };

    private static string Pluralize(string singular, string plural, int count) => count == 1 ? singular : plural;

    /// <summary>
    /// Topological order over project references (dependencies before dependents).
    /// Within one dependency level: quick-wins first (tier ascending), then by project-kind
    /// base weight ascending so the deepest hosting-model rewrite (wcf) is scheduled last —
    /// by then the team has the most porting experience — then score, then name.
    /// Projects that could not be analyzed are left out of the suggestion.
    /// </summary>
    private static IReadOnlyList<string> BuildMigrationOrder(
        ProjectScanResult scan, List<ProjectEffortEstimate> estimates)
    {
        Dictionary<string, ProjectEffortEstimate> byName =
            estimates.ToDictionary(e => e.ProjectName, StringComparer.OrdinalIgnoreCase);

        var analyzable = scan.Projects.Where(p => byName[p.ProjectName].Tier != TierUnknown).ToList();
        var names = new HashSet<string>(analyzable.Select(p => p.ProjectName), StringComparer.OrdinalIgnoreCase);

        // Level = longest reference chain below the project; references to projects
        // outside the solution (or unanalyzable ones) are ignored.
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<ProjectInfo>(analyzable);
        while (pending.Count > 0)
        {
            var ready = pending
                .Where(p => p.ProjectReferences.Where(names.Contains).All(levels.ContainsKey))
                .ToList();
            if (ready.Count == 0)
            {
                // Reference cycle: place the remainder in one final level rather than looping.
                int cycleLevel = levels.Count == 0 ? 0 : levels.Values.Max() + 1;
                foreach (ProjectInfo p in pending)
                {
                    levels[p.ProjectName] = cycleLevel;
                }

                break;
            }

            foreach (ProjectInfo p in ready)
            {
                int level = p.ProjectReferences.Where(names.Contains).Select(r => levels[r] + 1).DefaultIfEmpty(0).Max();
                levels[p.ProjectName] = level;
                pending.Remove(p);
            }
        }

        return analyzable
            .Select(p => byName[p.ProjectName])
            .OrderBy(e => levels[e.ProjectName])
            .ThenBy(e => TierRank(e.Tier))
            .ThenBy(e => ScoringWeights.BaseFor(e.ProjectKind))
            .ThenBy(e => e.Score)
            .ThenBy(e => e.ProjectName, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.ProjectName)
            .ToList();
    }

    private static int TierRank(string tier) => tier switch
    {
        TierQuickWin => 0,
        TierModerate => 1,
        _ => 2,
    };
}
