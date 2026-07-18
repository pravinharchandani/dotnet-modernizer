using System.Xml;
using System.Xml.Linq;
using DotnetModernizer.McpServer.Models;

namespace DotnetModernizer.McpServer.Scanning;

/// <summary>
/// Enumerates the projects of a solution and classifies each one.
/// </summary>
/// <remarks>
/// The .sln file is parsed as text rather than through Microsoft.Build.Construction:
/// MSBuild-based loading requires MSBuildLocator registration and resolves each
/// project's targets, which fails for legacy net472 projects on machines without
/// .NET Framework reference assemblies / VS build targets (e.g. Microsoft.WebApplication.targets).
/// The sln "Project(...)" line format has been stable for two decades and text parsing
/// keeps the scanner dependency-free and reliable. The csproj files themselves are read
/// as plain XML for the same reason. (CLAUDE.md's no-regex rule governs Roslyn source
/// analysis; sln/csproj are build metadata, not C# source, and are parsed structurally here.)
/// </remarks>
public static class SolutionScanner
{
    private const string LegacyMsbuildNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
    private const string WebApplicationProjectTypeGuid = "349c5851-65df-11da-9384-00065b846f21";

    public static ProjectScanResult Scan(string solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return Failure(solutionPath ?? string.Empty, "solutionPath must be a non-empty absolute path to a .sln file.");
        }

        if (!File.Exists(solutionPath))
        {
            return Failure(solutionPath, $"Solution file not found: {solutionPath}");
        }

        string solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;

        List<(string Name, string RelativePath)> entries;
        try
        {
            entries = ParseSolutionProjects(solutionPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure(solutionPath, $"Could not read solution file: {ex.Message}");
        }

        var projects = entries
            .Select(e => ScanProject(e.Name, e.RelativePath, solutionDir))
            .ToList();

        return new ProjectScanResult(solutionPath, projects, Summarize(projects));
    }

    private static ProjectScanResult Failure(string solutionPath, string error) =>
        new(solutionPath, [], new SolutionSummary(0, 0, null), error);

    /// <summary>
    /// Extracts csproj entries from sln "Project(...)" lines, e.g.:
    /// Project("{FAE04EC0-...}") = "LegacyShop.Web", "LegacyShop.Web\LegacyShop.Web.csproj", "{8F5A...}"
    /// Quoted segments are: [1] project-type guid, [3] name, [5] relative path, [7] project guid.
    /// Solution folders and non-csproj projects are skipped.
    /// </summary>
    private static List<(string Name, string RelativePath)> ParseSolutionProjects(string solutionPath)
    {
        var result = new List<(string, string)>();
        foreach (string line in File.ReadLines(solutionPath))
        {
            string trimmed = line.TrimStart();
            if (!trimmed.StartsWith("Project(", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = trimmed.Split('"');
            if (parts.Length < 6)
            {
                continue;
            }

            string name = parts[3];
            string relativePath = parts[5];
            if (relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                result.Add((name, relativePath));
            }
        }

        return result;
    }

    private static ProjectInfo ScanProject(string name, string relativePath, string solutionDir)
    {
        // sln files always use backslash separators; normalize for the current OS.
        string fullPath = Path.GetFullPath(Path.Combine(solutionDir, relativePath.Replace('\\', Path.DirectorySeparatorChar)));

        if (!File.Exists(fullPath))
        {
            return new ProjectInfo(name, relativePath, "unknown", null, "unknown", [],
                $"Project file not found: {fullPath}");
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(fullPath);
        }
        catch (XmlException ex)
        {
            return new ProjectInfo(name, relativePath, "unknown", null, "unknown", [],
                $"Malformed csproj XML: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ProjectInfo(name, relativePath, "unknown", null, "unknown", [],
                $"Could not read csproj: {ex.Message}");
        }

        XElement? root = doc.Root;
        if (root is null || root.Name.LocalName != "Project")
        {
            return new ProjectInfo(name, relativePath, "unknown", null, "unknown", [],
                "Malformed csproj: root element is not <Project>.");
        }

        bool hasSdkAttribute = root.Attribute("Sdk") is not null;
        bool hasLegacyNamespace = root.Name.NamespaceName == LegacyMsbuildNamespace;
        string style = !hasSdkAttribute && hasLegacyNamespace ? "legacy" : "sdk";

        string? targetFramework = ReadTargetFramework(root);
        var projectReferences = ReadProjectReferences(root);
        string kind = ClassifyProject(root);

        return new ProjectInfo(name, relativePath, style, targetFramework, kind, projectReferences);
    }

    private static string? ReadTargetFramework(XElement root)
    {
        string? Element(string localName) => root
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == localName && !string.IsNullOrWhiteSpace(e.Value))
            ?.Value.Trim();

        string? tf = Element("TargetFramework") ?? Element("TargetFrameworks");
        if (tf is not null)
        {
            return tf;
        }

        string? legacyVersion = Element("TargetFrameworkVersion");
        return legacyVersion is null ? null : NormalizeLegacyVersion(legacyVersion);
    }

    /// <summary>Maps legacy "v4.7.2" style versions to TFM form ("net472").</summary>
    private static string NormalizeLegacyVersion(string version)
    {
        string digits = string.Concat(version.TrimStart('v', 'V').Where(char.IsDigit));
        return digits.Length == 0 ? version : "net" + digits;
    }

    private static IReadOnlyList<string> ReadProjectReferences(XElement root)
    {
        return root
            .Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e =>
            {
                // Legacy projects usually carry an explicit <Name> child; fall back to the file name.
                string? nameChild = e.Elements()
                    .FirstOrDefault(c => c.Name.LocalName == "Name")
                    ?.Value.Trim();
                if (!string.IsNullOrEmpty(nameChild))
                {
                    return nameChild;
                }

                string? include = e.Attribute("Include")?.Value;
                return include is null ? null : Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));
            })
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();
    }

    private static string ClassifyProject(XElement root)
    {
        var assemblyReferences = root
            .Descendants()
            .Where(e => e.Name.LocalName is "Reference" or "PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Select(v => v.Split(',')[0].Trim()) // strip strong-name parts: "System.Web.Mvc, Version=..."
            .Where(v => v.Length > 0)
            .ToList();

        bool References(string prefix) => assemblyReferences.Any(r =>
            r.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            r.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));

        string projectTypeGuids = root
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "ProjectTypeGuids")
            ?.Value ?? string.Empty;

        if (References("xunit") || References("nunit") || References("NUnit") || References("MSTest") ||
            References("Microsoft.NET.Test.Sdk"))
        {
            return "test";
        }

        if (References("System.Web.Mvc") || References("System.Web") ||
            References("Microsoft.AspNetCore.App") ||
            projectTypeGuids.Contains(WebApplicationProjectTypeGuid, StringComparison.OrdinalIgnoreCase))
        {
            return "web";
        }

        if (References("System.ServiceModel"))
        {
            return "wcf";
        }

        string? outputType = root
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "OutputType")
            ?.Value.Trim();
        if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase))
        {
            return "console";
        }

        return "classlib";
    }

    private static SolutionSummary Summarize(List<ProjectInfo> projects)
    {
        int legacyCount = projects.Count(p => p.ProjectStyle == "legacy");

        string? lowest = projects
            .SelectMany(p => (p.TargetFramework ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(tf => tf.Trim())
            .Where(tf => tf.Length > 0)
            .OrderBy(tf => tf, FrameworkComparer.Instance)
            .FirstOrDefault();

        return new SolutionSummary(projects.Count, legacyCount, lowest);
    }

    /// <summary>
    /// Best-effort ordering of TFMs by their embedded version number, so net472
    /// sorts below net8.0 and net10.0. Non-numeric monikers sort last.
    /// </summary>
    private sealed class FrameworkComparer : IComparer<string>
    {
        public static readonly FrameworkComparer Instance = new();

        public int Compare(string? x, string? y) => Rank(x).CompareTo(Rank(y));

        private static double Rank(string? tfm)
        {
            if (string.IsNullOrEmpty(tfm))
            {
                return double.MaxValue;
            }

            // net472 -> 4.72, net8.0 -> 8.0, netstandard2.0 -> 2.0, netcoreapp3.1 -> 3.1
            string tail = tfm.ToLowerInvariant()
                .Replace("netstandard", string.Empty)
                .Replace("netcoreapp", string.Empty)
                .Replace("net", string.Empty);

            string digitsAndDots = string.Concat(tail.TakeWhile(c => char.IsDigit(c) || c == '.'));
            if (digitsAndDots.Length == 0)
            {
                return double.MaxValue;
            }

            if (digitsAndDots.Contains('.'))
            {
                return double.TryParse(digitsAndDots, System.Globalization.CultureInfo.InvariantCulture, out double v)
                    ? v
                    : double.MaxValue;
            }

            // Legacy compressed form: "472" means 4.72, "48" means 4.8.
            return double.Parse(digitsAndDots[..1] + "." + digitsAndDots[1..].PadRight(1, '0'),
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
