using System.ComponentModel;
using DotnetModernizer.McpServer.Analysis;
using DotnetModernizer.McpServer.Models;
using ModelContextProtocol.Server;

namespace DotnetModernizer.McpServer.Tools;

[McpServerToolType]
public static class AnalyzeNugetCompatTool
{
    [McpServerTool(Name = "analyze_nuget_compat", UseStructuredContent = true)]
    [Description("Analyzes every NuGet package a project references (packages.config and " +
                 "PackageReference are both supported) and reports .NET 10 compatibility per " +
                 "package: \"compatible\", \"compatible-with-upgrade\", \"incompatible\", or " +
                 "\"unknown\", with a recommended action and an optional replacement package. " +
                 "Verdicts come from a built-in catalog of common legacy packages, then from " +
                 "the NuGet V3 API when the package is unknown and the network is available; " +
                 "it never guesses. Never throws: errors are reported in the result's error field.")]
    public static NugetCompatResult AnalyzeNugetCompat(
        [Description("Absolute path to a .csproj file")] string projectPath)
    {
        return NugetCompatAnalyzer.Analyze(projectPath);
    }
}
