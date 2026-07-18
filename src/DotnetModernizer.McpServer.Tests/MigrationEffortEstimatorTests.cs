using DotnetModernizer.McpServer.Analysis;
using DotnetModernizer.McpServer.Models;
using DotnetModernizer.McpServer.Services;
using Xunit;

namespace DotnetModernizer.McpServer.Tests;

public class MigrationEffortEstimatorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("modernizer-effort-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    /// <summary>Deterministic "no network" client so package verdicts come from the built-in catalog only.</summary>
    private sealed class OfflineClient : INuGetMetadataClient
    {
        public NuGetPackageMetadata? TryGetLatest(string packageId) => null;
    }

    private static string FixtureSolutionPath()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "test-fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "test-fixtures", "LegacyShop", "LegacyShop.sln");
    }

    private static MigrationEffortResult EstimateFixture() =>
        MigrationEffortEstimator.Estimate(FixtureSolutionPath(), new OfflineClient());

    private static ProjectEffortEstimate ProjectOf(MigrationEffortResult result, string name)
    {
        ProjectEffortEstimate? project = result.Projects.SingleOrDefault(p => p.ProjectName == name);
        Assert.NotNull(project);
        return project;
    }

    // --- The spec's acceptance cases ---

    [Fact]
    public void Estimate_LegacyShop_Utils_IsQuickWin_AndFirstInOrder()
    {
        MigrationEffortResult result = EstimateFixture();

        Assert.Null(result.Error);
        Assert.Equal("quick-win", ProjectOf(result, "LegacyShop.Utils").Tier);
        Assert.Equal("LegacyShop.Utils", result.SuggestedMigrationOrder.First());
    }

    [Fact]
    public void Estimate_LegacyShop_Services_IsComplex_AndLastInOrder()
    {
        MigrationEffortResult result = EstimateFixture();

        Assert.Null(result.Error);
        Assert.Equal("complex", ProjectOf(result, "LegacyShop.Services").Tier);
        Assert.Equal("LegacyShop.Services", result.SuggestedMigrationOrder.Last());
    }

    // --- Scoring model on the fixture ---

    [Fact]
    public void Estimate_LegacyShop_ProjectTiers()
    {
        MigrationEffortResult result = EstimateFixture();

        // Core: classlib(1) + 2 blocker families (Remoting, BinaryFormatter = +6),
        // ×1.2 (Services and Web depend on it), +1 legacy = 9.4 → moderate.
        Assert.Equal("moderate", ProjectOf(result, "LegacyShop.Core").Tier);

        // Web: web(8) + 1 blocker family (System.Web = +3) + 3 incompatible packages (+6)
        // + 2 upgradable packages (+1), +1 legacy = 19 → complex.
        Assert.Equal("complex", ProjectOf(result, "LegacyShop.Web").Tier);
    }

    [Fact]
    public void Estimate_LegacyShop_Utils_ScoreReflectsDependencyFactorAndLegacyFlat()
    {
        MigrationEffortResult result = EstimateFixture();

        // classlib(1) × 1.1 (Web depends on it) + 1 legacy flat = 2.1.
        Assert.Equal(2.1, ProjectOf(result, "LegacyShop.Utils").Score);
    }

    [Fact]
    public void Estimate_LegacyShop_BlockerFamiliesAreCountedDistinct_NotPerFinding()
    {
        MigrationEffortResult result = EstimateFixture();

        // Web has dozens of System.Web.* findings but they are ONE family: its blocker
        // driver must carry exactly one family's points.
        ProjectEffortEstimate web = ProjectOf(result, "LegacyShop.Web");
        EffortDriver? blockerDriver = web.TopDrivers.SingleOrDefault(d => d.Name.Contains("blocker API"));
        if (blockerDriver is not null)
        {
            Assert.Equal("1 blocker API family", blockerDriver.Name);
            Assert.Equal(3, blockerDriver.Points);
        }

        // Services uses ServiceContract + OperationContract = two distinct families.
        ProjectEffortEstimate services = ProjectOf(result, "LegacyShop.Services");
        Assert.Contains(services.TopDrivers, d => d.Name == "2 blocker API families" && d.Points == 6);
    }

    [Fact]
    public void Estimate_LegacyShop_TopDrivers_AtMostThree_AndNamed()
    {
        MigrationEffortResult result = EstimateFixture();

        foreach (ProjectEffortEstimate project in result.Projects)
        {
            Assert.InRange(project.TopDrivers.Count, 1, 3);
            Assert.All(project.TopDrivers, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
        }

        // The web project's biggest single driver is the hosting-model base.
        Assert.Equal("web project base effort", ProjectOf(result, "LegacyShop.Web").TopDrivers[0].Name);
    }

    // --- Migration order and rollup ---

    [Fact]
    public void Estimate_LegacyShop_Order_IsTopological_DependenciesFirst()
    {
        MigrationEffortResult result = EstimateFixture();
        List<string> order = result.SuggestedMigrationOrder.ToList();

        Assert.Equal(4, order.Count);
        Assert.True(order.IndexOf("LegacyShop.Core") < order.IndexOf("LegacyShop.Services"));
        Assert.True(order.IndexOf("LegacyShop.Core") < order.IndexOf("LegacyShop.Web"));
        Assert.True(order.IndexOf("LegacyShop.Utils") < order.IndexOf("LegacyShop.Web"));
    }

    [Fact]
    public void Estimate_LegacyShop_Summary_CountsTiersAndTotalsScores()
    {
        MigrationEffortResult result = EstimateFixture();

        Assert.Equal(1, result.Summary.QuickWinCount);
        Assert.Equal(1, result.Summary.ModerateCount);
        Assert.Equal(2, result.Summary.ComplexCount);
        Assert.Equal(Math.Round(result.Projects.Sum(p => p.Score), 2), result.Summary.TotalScore);
    }

    // --- Error handling (CLAUDE.md contract) ---

    [Fact]
    public void Estimate_MissingSolution_ReturnsError()
    {
        string missing = Path.Combine(_tempDir, "Nope.sln");

        MigrationEffortResult result = MigrationEffortEstimator.Estimate(missing, new OfflineClient());

        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Projects);
        Assert.Empty(result.SuggestedMigrationOrder);
    }

    [Fact]
    public void Estimate_EmptySolution_ReturnsEmptyResultWithoutError()
    {
        string sln = Path.Combine(_tempDir, "Empty.sln");
        File.WriteAllText(sln, "Microsoft Visual Studio Solution File, Format Version 12.00\n");

        MigrationEffortResult result = MigrationEffortEstimator.Estimate(sln, new OfflineClient());

        Assert.Null(result.Error);
        Assert.Empty(result.Projects);
        Assert.Empty(result.SuggestedMigrationOrder);
        Assert.Equal(0, result.Summary.TotalScore);
    }

    [Fact]
    public void Estimate_SolutionWithMalformedProject_ReportsProjectErrorAndSkipsItInOrder()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Bad"));
        File.WriteAllText(Path.Combine(_tempDir, "Bad", "Bad.csproj"), "<Project><ItemGroup>");
        string sln = Path.Combine(_tempDir, "Mixed.sln");
        File.WriteAllText(sln,
            "Microsoft Visual Studio Solution File, Format Version 12.00\n" +
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Bad\", \"Bad\\Bad.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\n" +
            "EndProject\n");

        MigrationEffortResult result = MigrationEffortEstimator.Estimate(sln, new OfflineClient());

        Assert.Null(result.Error);
        ProjectEffortEstimate bad = ProjectOf(result, "Bad");
        Assert.NotNull(bad.Error);
        Assert.Equal("unknown", bad.Tier);
        Assert.Empty(result.SuggestedMigrationOrder);
    }
}
