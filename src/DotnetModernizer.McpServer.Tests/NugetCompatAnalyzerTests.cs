using DotnetModernizer.McpServer.Analysis;
using DotnetModernizer.McpServer.Models;
using Xunit;

namespace DotnetModernizer.McpServer.Tests;

public class NugetCompatAnalyzerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("modernizer-nuget-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    /// <summary>Deterministic "no network" client: every lookup misses.</summary>
    private sealed class OfflineClient : INuGetMetadataClient
    {
        public NuGetPackageMetadata? TryGetLatest(string packageId) => null;
    }

    /// <summary>Canned network responses keyed by package id.</summary>
    private sealed class FakeClient(Dictionary<string, NuGetPackageMetadata> responses) : INuGetMetadataClient
    {
        public NuGetPackageMetadata? TryGetLatest(string packageId) =>
            responses.TryGetValue(packageId, out NuGetPackageMetadata? metadata) ? metadata : null;
    }

    private static string FixtureProjectPath(string projectName)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "test-fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "test-fixtures", "LegacyShop", projectName, projectName + ".csproj");
    }

    private string WriteTempProject(string csprojContent, string? packagesConfigContent = null)
    {
        string projectDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDir);
        string csprojPath = Path.Combine(projectDir, "Temp.csproj");
        File.WriteAllText(csprojPath, csprojContent);
        if (packagesConfigContent is not null)
        {
            File.WriteAllText(Path.Combine(projectDir, "packages.config"), packagesConfigContent);
        }

        return csprojPath;
    }

    private static PackageCompatibility PackageOf(NugetCompatResult result, string packageId)
    {
        PackageCompatibility? package = result.Packages
            .SingleOrDefault(p => p.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(package);
        return package;
    }

    // --- LegacyShop.Web packages.config (the spec's acceptance cases) ---

    [Fact]
    public void Analyze_WebPackagesConfig_AspNetMvc_IsIncompatible()
    {
        NugetCompatResult result = NugetCompatAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Web"), new OfflineClient());

        Assert.Null(result.Error);
        PackageCompatibility mvc = PackageOf(result, "Microsoft.AspNet.Mvc");
        Assert.Equal("incompatible", mvc.Status);
        Assert.Equal("5.2.7", mvc.CurrentVersion);
        Assert.Contains("ASP.NET Core", mvc.RecommendedAction);
    }

    [Fact]
    public void Analyze_WebPackagesConfig_NewtonsoftJson608_IsCompatibleWithUpgrade()
    {
        NugetCompatResult result = NugetCompatAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Web"), new OfflineClient());

        Assert.Null(result.Error);
        PackageCompatibility json = PackageOf(result, "Newtonsoft.Json");
        Assert.Equal("compatible-with-upgrade", json.Status);
        Assert.Equal("6.0.8", json.CurrentVersion);
        Assert.Contains("System.Text.Json", json.RecommendedAction);
    }

    [Fact]
    public void Analyze_WebPackagesConfig_EntityFramework620_IsCompatibleWithUpgrade()
    {
        NugetCompatResult result = NugetCompatAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Web"), new OfflineClient());

        Assert.Null(result.Error);
        PackageCompatibility ef = PackageOf(result, "EntityFramework");
        Assert.Equal("compatible-with-upgrade", ef.Status);
        Assert.Equal("6.2.0", ef.CurrentVersion);
        Assert.Contains("EF Core", ef.RecommendedAction);
        Assert.Equal("Microsoft.EntityFrameworkCore", ef.ReplacementPackage);
    }

    [Fact]
    public void Analyze_WebPackagesConfig_ReportsAllFivePackages()
    {
        NugetCompatResult result = NugetCompatAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Web"), new OfflineClient());

        Assert.Null(result.Error);
        Assert.Equal(5, result.Packages.Count);
        Assert.Equal("incompatible", PackageOf(result, "Microsoft.AspNet.Razor").Status);
        Assert.Equal("incompatible", PackageOf(result, "Microsoft.AspNet.WebPages").Status);
    }

    // --- SDK-style PackageReference parsing ---

    [Fact]
    public void Analyze_SdkStylePackageReference_ModernNewtonsoft_IsCompatible()
    {
        string csproj = WriteTempProject(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(csproj, new OfflineClient());

        Assert.Null(result.Error);
        PackageCompatibility json = PackageOf(result, "Newtonsoft.Json");
        Assert.Equal("compatible", json.Status);
        Assert.Equal("13.0.3", json.CurrentVersion);
    }

    [Fact]
    public void Analyze_PackageReference_VersionAsChildElement_IsParsed()
    {
        string csproj = WriteTempProject(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json">
                  <Version>13.0.3</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(csproj, new OfflineClient());

        Assert.Null(result.Error);
        Assert.Equal("13.0.3", PackageOf(result, "Newtonsoft.Json").CurrentVersion);
    }

    // --- Layered strategy: catalog miss → network → unknown ---

    [Fact]
    public void Analyze_UnknownPackageOffline_ReturnsUnknown()
    {
        string csproj = WriteTempProject(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Contoso.Internal.Widgets" Version="1.2.3" />
              </ItemGroup>
            </Project>
            """);

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(csproj, new OfflineClient());

        Assert.Null(result.Error);
        PackageCompatibility package = PackageOf(result, "Contoso.Internal.Widgets");
        Assert.Equal("unknown", package.Status);
        Assert.Null(package.ReplacementPackage);
    }

    [Fact]
    public void Analyze_UnknownPackage_NetworkShowsModernFrameworks_IsCompatibleWithUpgrade()
    {
        string csproj = WriteTempProject(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Contoso.Internal.Widgets" Version="1.2.3" />
              </ItemGroup>
            </Project>
            """);
        var client = new FakeClient(new Dictionary<string, NuGetPackageMetadata>
        {
            ["Contoso.Internal.Widgets"] = new("9.9.9", ["net45", ".NETStandard2.0"]),
        });

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(csproj, client);

        Assert.Null(result.Error);
        PackageCompatibility package = PackageOf(result, "Contoso.Internal.Widgets");
        Assert.Equal("compatible-with-upgrade", package.Status);
        Assert.Contains("9.9.9", package.RecommendedAction);
    }

    [Fact]
    public void Analyze_UnknownPackage_AlreadyOnLatestModernVersion_IsCompatible()
    {
        string csproj = WriteTempProject(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Contoso.Internal.Widgets" Version="9.9.9" />
              </ItemGroup>
            </Project>
            """);
        var client = new FakeClient(new Dictionary<string, NuGetPackageMetadata>
        {
            ["Contoso.Internal.Widgets"] = new("9.9.9", ["netstandard2.0", "net8.0"]),
        });

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(csproj, client);

        Assert.Null(result.Error);
        Assert.Equal("compatible", PackageOf(result, "Contoso.Internal.Widgets").Status);
    }

    [Fact]
    public void Analyze_UnknownPackage_NetworkShowsOnlyLegacyFrameworks_IsIncompatible()
    {
        string csproj = WriteTempProject(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Contoso.Legacy.Widgets" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        var client = new FakeClient(new Dictionary<string, NuGetPackageMetadata>
        {
            ["Contoso.Legacy.Widgets"] = new("2.0.0", [".NETFramework4.7.2"]),
        });

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(csproj, client);

        Assert.Null(result.Error);
        Assert.Equal("incompatible", PackageOf(result, "Contoso.Legacy.Widgets").Status);
    }

    [Fact]
    public void Analyze_UnknownPackage_NetworkHasNoFrameworkData_ReturnsUnknown()
    {
        string csproj = WriteTempProject(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Contoso.Opaque.Widgets" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);
        var client = new FakeClient(new Dictionary<string, NuGetPackageMetadata>
        {
            ["Contoso.Opaque.Widgets"] = new("1.0.0", []),
        });

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(csproj, client);

        Assert.Null(result.Error);
        Assert.Equal("unknown", PackageOf(result, "Contoso.Opaque.Widgets").Status);
    }

    // --- Error handling (CLAUDE.md contract) ---

    [Fact]
    public void Analyze_MissingProjectFile_ReturnsError()
    {
        string missing = Path.Combine(_tempDir, "Nope", "Nope.csproj");

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(missing, new OfflineClient());

        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public void Analyze_MalformedCsproj_ReturnsError()
    {
        string csproj = WriteTempProject("<Project><ItemGroup>");

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(csproj, new OfflineClient());

        Assert.NotNull(result.Error);
        Assert.Contains("Malformed", result.Error);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public void Analyze_MalformedPackagesConfig_ReturnsError()
    {
        string csproj = WriteTempProject(
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>",
            packagesConfigContent: "<packages><package id=");

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(csproj, new OfflineClient());

        Assert.NotNull(result.Error);
        Assert.Contains("Malformed", result.Error);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public void Analyze_ProjectWithNoPackages_ReturnsEmptyListWithoutError()
    {
        string csproj = WriteTempProject("<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        NugetCompatResult result = NugetCompatAnalyzer.Analyze(csproj, new OfflineClient());

        Assert.Null(result.Error);
        Assert.Empty(result.Packages);
    }
}
