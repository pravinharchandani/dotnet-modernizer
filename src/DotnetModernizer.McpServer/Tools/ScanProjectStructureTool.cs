using System.ComponentModel;
using DotnetModernizer.McpServer.Models;
using DotnetModernizer.McpServer.Scanning;
using ModelContextProtocol.Server;

namespace DotnetModernizer.McpServer.Tools;

[McpServerToolType]
public static class ScanProjectStructureTool
{
    [McpServerTool(Name = "scan_project_structure", UseStructuredContent = true)]
    [Description("Parses a .sln file and reports every project's style (sdk/legacy), target framework, " +
                 "best-effort kind (web/wcf/classlib/console/test), and project references, " +
                 "plus a solution-level summary. Never throws: errors are reported in the result's " +
                 "error fields.")]
    public static ProjectScanResult ScanProjectStructure(
        [Description("Absolute path to a .sln file")] string solutionPath)
    {
        return SolutionScanner.Scan(solutionPath);
    }
}
