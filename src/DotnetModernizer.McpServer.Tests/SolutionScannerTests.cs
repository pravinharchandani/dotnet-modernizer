using DotnetModernizer.McpServer.Models;
using DotnetModernizer.McpServer.Scanning;
using Xunit;

namespace DotnetModernizer.McpServer.Tests;

public class SolutionScannerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("modernizer-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static string FixtureSolutionPath()
    {
        // Walk up from the test binary to the repo root (identified by test-fixtures/).
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "test-fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "test-fixtures", "LegacyShop", "LegacyShop.sln");
    }

    [Fact]
    public void Scan_LegacyShopFixture_ReportsAllProjectsWithCorrectMetadata()
    {
        ProjectScanResult result = SolutionScanner.Scan(FixtureSolutionPath());

        Assert.Null(result.Error);
        Assert.Equal(4, result.SolutionSummary.TotalProjects);
        Assert.Equal(4, result.SolutionSummary.LegacyStyleCount);
        Assert.Equal("net472", result.SolutionSummary.LowestFramework);

        Assert.All(result.Projects, p => Assert.Null(p.Error));
        Assert.All(result.Projects, p => Assert.Equal("legacy", p.ProjectStyle));
        Assert.All(result.Projects, p => Assert.Equal("net472", p.TargetFramework));

        ProjectInfo web = Assert.Single(result.Projects, p => p.ProjectName == "LegacyShop.Web");
        Assert.Equal("web", web.ProjectKind);
        Assert.Contains("LegacyShop.Core", web.ProjectReferences);
        Assert.Contains("LegacyShop.Utils", web.ProjectReferences);

        ProjectInfo services = Assert.Single(result.Projects, p => p.ProjectName == "LegacyShop.Services");
        Assert.Equal("wcf", services.ProjectKind);
        Assert.Contains("LegacyShop.Core", services.ProjectReferences);

        ProjectInfo utils = Assert.Single(result.Projects, p => p.ProjectName == "LegacyShop.Utils");
        Assert.Equal("classlib", utils.ProjectKind);
        Assert.Empty(utils.ProjectReferences);
    }

    [Fact]
    public void Scan_MissingSolutionFile_ReturnsSolutionLevelError()
    {
        string missing = Path.Combine(_tempDir, "DoesNotExist.sln");

        ProjectScanResult result = SolutionScanner.Scan(missing);

        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Projects);
        Assert.Equal(0, result.SolutionSummary.TotalProjects);
    }

    [Fact]
    public void Scan_MalformedCsproj_ReportsProjectLevelErrorWithoutFailingSolution()
    {
        string projectDir = Path.Combine(_tempDir, "Broken");
        Directory.CreateDirectory(projectDir);
        File.WriteAllText(Path.Combine(projectDir, "Broken.csproj"),
            "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><PropertyGroup>"); // unclosed tags

        string slnPath = Path.Combine(_tempDir, "Broken.sln");
        File.WriteAllText(slnPath,
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Broken", "Broken\Broken.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            """);

        ProjectScanResult result = SolutionScanner.Scan(slnPath);

        Assert.Null(result.Error);
        ProjectInfo broken = Assert.Single(result.Projects);
        Assert.NotNull(broken.Error);
        Assert.Contains("Malformed", broken.Error);
        Assert.Equal("unknown", broken.ProjectStyle);
        Assert.Equal(1, result.SolutionSummary.TotalProjects);
        Assert.Equal(0, result.SolutionSummary.LegacyStyleCount);
    }

    [Fact]
    public void Scan_EmptySolution_ReturnsZeroCountsWithoutError()
    {
        string slnPath = Path.Combine(_tempDir, "Empty.sln");
        File.WriteAllText(slnPath,
            """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio 15
            Global
            EndGlobal
            """);

        ProjectScanResult result = SolutionScanner.Scan(slnPath);

        Assert.Null(result.Error);
        Assert.Empty(result.Projects);
        Assert.Equal(0, result.SolutionSummary.TotalProjects);
        Assert.Null(result.SolutionSummary.LowestFramework);
    }
}
