using DotnetModernizer.McpServer.Analysis;
using DotnetModernizer.McpServer.Models;
using Xunit;

namespace DotnetModernizer.McpServer.Tests;

public class BreakingApiAnalyzerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("modernizer-api-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

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

    private string WriteTempProject(params (string FileName, string Source)[] files)
    {
        string projectDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDir);
        string csprojPath = Path.Combine(projectDir, "Temp.csproj");
        File.WriteAllText(csprojPath, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        foreach ((string fileName, string source) in files)
        {
            File.WriteAllText(Path.Combine(projectDir, fileName), source);
        }

        return csprojPath;
    }

    // --- Trap tests: comments and string literals must never produce findings ---

    [Fact]
    public void Analyze_Core_CommentAndStringLiteralTraps_ProduceZeroFindings()
    {
        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Core"));

        Assert.Null(result.Error);
        // PricingCalculator.cs mentions System.Web in a comment and BinaryFormatter in a
        // string literal but uses neither API.
        Assert.DoesNotContain(result.Findings,
            f => Path.GetFileName(f.FilePath) == "PricingCalculator.cs");
    }

    [Fact]
    public void Analyze_Core_RealBinaryFormatterUsage_IsDetectedAsBlocker()
    {
        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Core"));

        Assert.Null(result.Error);
        BreakingApiFinding[] formatterFindings = result.Findings
            .Where(f => Path.GetFileName(f.FilePath) == "LegacyOrderArchiver.cs" &&
                        f.ApiName.Contains("BinaryFormatter"))
            .ToArray();
        Assert.NotEmpty(formatterFindings);
        Assert.All(formatterFindings, f => Assert.Equal("blocker", f.Severity));
        Assert.All(formatterFindings, f => Assert.Contains("System.Text.Json", f.Recommendation));
    }

    [Fact]
    public void Analyze_Core_RemotingUsage_IsDetectedAsBlocker()
    {
        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Core"));

        Assert.Null(result.Error);
        BreakingApiFinding[] remotingFindings = result.Findings
            .Where(f => Path.GetFileName(f.FilePath) == "RemotingOrderChannel.cs" &&
                        f.ApiName.StartsWith("System.Runtime.Remoting"))
            .ToArray();
        Assert.NotEmpty(remotingFindings);
        Assert.All(remotingFindings, f => Assert.Equal("blocker", f.Severity));
    }

    // --- Alias handling ---

    [Fact]
    public void Analyze_Web_SystemWebMvcThroughAlias_IsDetected()
    {
        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Web"));

        Assert.Null(result.Error);
        // OrdersController.cs only reaches System.Web.Mvc through "using SWM = System.Web.Mvc;".
        BreakingApiFinding[] aliasFindings = result.Findings
            .Where(f => Path.GetFileName(f.FilePath) == "OrdersController.cs" &&
                        f.ApiName.StartsWith("System.Web.Mvc"))
            .ToArray();
        Assert.NotEmpty(aliasFindings);
        Assert.All(aliasFindings, f => Assert.Equal("blocker", f.Severity));
        // The alias-qualified base class "SWM.Controller" (line 10) must itself be resolved.
        Assert.Contains(aliasFindings, f => f.ApiName == "System.Web.Mvc.Controller");
    }

    [Fact]
    public void Analyze_Web_ReportsSystemWebFindingsAcrossControllers()
    {
        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Web"));

        Assert.Null(result.Error);
        Assert.Contains(result.Findings,
            f => Path.GetFileName(f.FilePath) == "HomeController.cs" &&
                 f.ApiName.StartsWith("System.Web"));
        Assert.All(result.Findings, f => Assert.True(f.Line >= 1));
        Assert.All(result.Findings, f => Assert.Contains(f.DetectionLevel, new[] { "semantic", "syntactic" }));
    }

    // --- Clean project ---

    [Fact]
    public void Analyze_Utils_ProducesZeroFindings()
    {
        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Utils"));

        Assert.Null(result.Error);
        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.BlockerCount);
        Assert.Equal(0, result.Summary.WarningCount);
        Assert.True(result.Summary.FilesScanned >= 2);
    }

    // --- WCF attributes ---

    [Fact]
    public void Analyze_Services_WcfServerAttributes_AreDetectedAsBlockers()
    {
        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Services"));

        Assert.Null(result.Error);
        Assert.Contains(result.Findings,
            f => f.ApiName == "System.ServiceModel.ServiceContractAttribute" && f.Severity == "blocker");
        Assert.Contains(result.Findings,
            f => f.ApiName == "System.ServiceModel.OperationContractAttribute" && f.Severity == "blocker");
        // DataContract/DataMember survive on .NET 10 and must not be flagged.
        Assert.DoesNotContain(result.Findings, f => f.ApiName.Contains("DataContract"));
        Assert.DoesNotContain(result.Findings, f => f.ApiName.Contains("DataMember"));
    }

    // --- Severity filter ---

    [Fact]
    public void Analyze_BlockerFilter_ExcludesWarnings()
    {
        string csproj = WriteTempProject(("Mixed.cs",
            """
            using System;

            public class Mixed
            {
                public void Run()
                {
                    AppDomain.CreateDomain("sandbox");
                    var stale = new System.Runtime.Remoting.ObjectHandle(null);
                }
            }
            """));

        BreakingApiScanResult all = BreakingApiAnalyzer.Analyze(csproj, "all");
        BreakingApiScanResult blockers = BreakingApiAnalyzer.Analyze(csproj, "blocker");
        BreakingApiScanResult warnings = BreakingApiAnalyzer.Analyze(csproj, "warning");

        Assert.Null(all.Error);
        Assert.Contains(all.Findings, f => f.Severity == "blocker");
        Assert.Contains(all.Findings, f => f.Severity == "warning");

        Assert.NotEmpty(blockers.Findings);
        Assert.All(blockers.Findings, f => Assert.Equal("blocker", f.Severity));

        Assert.NotEmpty(warnings.Findings);
        Assert.All(warnings.Findings, f => Assert.Equal("warning", f.Severity));
    }

    [Fact]
    public void Analyze_InvalidSeverityFilter_ReturnsError()
    {
        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(FixtureProjectPath("LegacyShop.Utils"), "bogus");

        Assert.NotNull(result.Error);
        Assert.Contains("severityFilter", result.Error);
        Assert.Empty(result.Findings);
    }

    // --- Catalog specifics ---

    [Fact]
    public void Analyze_AppDomainCreateDomain_IsWarningDetectedSemantically()
    {
        string csproj = WriteTempProject(("Sandbox.cs",
            """
            using System;

            public class Sandbox
            {
                public void Run()
                {
                    AppDomain.CreateDomain("sandbox");
                }
            }
            """));

        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(csproj);

        Assert.Null(result.Error);
        BreakingApiFinding finding = Assert.Single(result.Findings);
        Assert.Equal("System.AppDomain.CreateDomain", finding.ApiName);
        Assert.Equal("warning", finding.Severity);
        Assert.Equal("semantic", finding.DetectionLevel);
        Assert.Equal(7, finding.Line);
    }

    [Fact]
    public void Analyze_HttpUtility_IsExemptFromSystemWebRule()
    {
        string csproj = WriteTempProject(("Encoder.cs",
            """
            public class Encoder
            {
                public string Encode(string value)
                {
                    // Exempt type: available on .NET 10 via System.Web.HttpUtility.dll.
                    return System.Web.HttpUtility.HtmlEncode(value);
                }

                public object Forbidden()
                {
                    return typeof(System.Web.Caching.Cache);
                }
            }
            """));

        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(csproj);

        Assert.Null(result.Error);
        BreakingApiFinding finding = Assert.Single(result.Findings);
        Assert.StartsWith("System.Web.Caching", finding.ApiName);
        Assert.DoesNotContain(result.Findings, f => f.ApiName.Contains("HttpUtility"));
    }

    [Fact]
    public void Analyze_MicrosoftVisualBasicFromCSharp_IsWarning()
    {
        string csproj = WriteTempProject(("VbInterop.cs",
            """
            public class VbInterop
            {
                public string Money(decimal amount)
                {
                    return Microsoft.VisualBasic.Strings.FormatCurrency(amount);
                }
            }
            """));

        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(csproj);

        Assert.Null(result.Error);
        Assert.NotEmpty(result.Findings);
        Assert.All(result.Findings, f =>
        {
            Assert.StartsWith("Microsoft.VisualBasic", f.ApiName);
            Assert.Equal("warning", f.Severity);
        });
    }

    // --- Error handling (CLAUDE.md contract) ---

    [Fact]
    public void Analyze_MissingProjectFile_ReturnsError()
    {
        string missing = Path.Combine(_tempDir, "Nope", "Nope.csproj");

        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(missing);

        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Analyze_MalformedCsproj_ReturnsError()
    {
        string projectDir = Path.Combine(_tempDir, "Broken");
        Directory.CreateDirectory(projectDir);
        string csprojPath = Path.Combine(projectDir, "Broken.csproj");
        File.WriteAllText(csprojPath, "<Project><PropertyGroup>"); // unclosed tags

        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(csprojPath);

        Assert.NotNull(result.Error);
        Assert.Contains("Malformed", result.Error);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Analyze_ProjectWithNoSourceFiles_ReturnsEmptyFindingsWithoutError()
    {
        string csproj = WriteTempProject();

        BreakingApiScanResult result = BreakingApiAnalyzer.Analyze(csproj);

        Assert.Null(result.Error);
        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.FilesScanned);
    }
}
