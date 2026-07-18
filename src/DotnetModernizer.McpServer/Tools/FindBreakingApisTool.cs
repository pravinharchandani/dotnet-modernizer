using System.ComponentModel;
using DotnetModernizer.McpServer.Analysis;
using DotnetModernizer.McpServer.Models;
using ModelContextProtocol.Server;

namespace DotnetModernizer.McpServer.Tools;

[McpServerToolType]
public static class FindBreakingApisTool
{
    [McpServerTool(Name = "find_breaking_apis", UseStructuredContent = true)]
    [Description("Analyzes the C# sources of a project with Roslyn and reports every use of an API " +
                 "that is removed or fundamentally changed on .NET 10 (System.Web, WCF server " +
                 "attributes, BinaryFormatter, Remoting, EnterpriseServices, ...). Each finding " +
                 "carries file, line, severity (blocker/warning), a migration recommendation, and " +
                 "whether it was detected semantically or syntactically. Never throws: errors are " +
                 "reported in the result's error field.")]
    public static BreakingApiScanResult FindBreakingApis(
        [Description("Absolute path to a .csproj file")] string projectPath,
        [Description("Optional severity filter: \"all\" (default), \"blocker\", or \"warning\"")]
        string severityFilter = "all")
    {
        return BreakingApiAnalyzer.Analyze(projectPath, severityFilter);
    }
}
