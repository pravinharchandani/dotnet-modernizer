using System.Xml;
using System.Xml.Linq;
using DotnetModernizer.McpServer.Models;

namespace DotnetModernizer.McpServer.Analysis;

/// <summary>
/// Decides .NET 10 compatibility for every NuGet package a project references.
/// </summary>
/// <remarks>
/// Layered strategy, because network access may be unavailable:
///  1. <see cref="KnownPackageCatalog"/> for the common legacy suspects (offline, firm).
///  2. NuGet flat-container metadata via <see cref="INuGetMetadataClient"/>: the latest
///     version's target frameworks decide (netstandard2.x and net5.0+ count as
///     compatible because net10.0 consumes them).
///  3. Status "unknown" — never guess.
/// </remarks>
public static class NugetCompatAnalyzer
{
    public const string Compatible = "compatible";
    public const string CompatibleWithUpgrade = "compatible-with-upgrade";
    public const string Incompatible = "incompatible";
    public const string Unknown = "unknown";

    public static NugetCompatResult Analyze(string projectPath, INuGetMetadataClient? client = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return Failure(projectPath ?? string.Empty, "projectPath must be a non-empty absolute path to a .csproj file.");
        }

        if (!File.Exists(projectPath))
        {
            return Failure(projectPath, $"Project file not found: {projectPath}");
        }

        XDocument csproj;
        try
        {
            csproj = XDocument.Load(projectPath);
        }
        catch (XmlException ex)
        {
            return Failure(projectPath, $"Malformed csproj XML: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure(projectPath, $"Could not read csproj: {ex.Message}");
        }

        // Both reference styles are collected: a mid-migration project can carry a
        // packages.config next to a csproj that already uses PackageReference.
        var references = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string packagesConfigPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, "packages.config");
        if (File.Exists(packagesConfigPath))
        {
            try
            {
                foreach (XElement package in XDocument.Load(packagesConfigPath).Descendants("package"))
                {
                    string? id = package.Attribute("id")?.Value;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        references[id] = package.Attribute("version")?.Value ?? string.Empty;
                    }
                }
            }
            catch (XmlException ex)
            {
                return Failure(projectPath, $"Malformed packages.config XML: {ex.Message}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failure(projectPath, $"Could not read packages.config: {ex.Message}");
            }
        }

        // LocalName-based match so both SDK-style (no namespace) and namespaced legacy
        // csproj files are covered.
        foreach (XElement reference in csproj.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
        {
            string? id = reference.Attribute("Include")?.Value ?? reference.Attribute("Update")?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            string version = reference.Attribute("Version")?.Value
                ?? reference.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value
                ?? string.Empty;
            references.TryAdd(id, version);
        }

        client ??= new NuGetFlatContainerClient();
        List<PackageCompatibility> packages = references
            .Select(r => Classify(r.Key, r.Value, client))
            .OrderBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new NugetCompatResult(projectPath, packages);
    }

    private static NugetCompatResult Failure(string projectPath, string error) =>
        new(projectPath, [], error);

    private static PackageCompatibility Classify(string packageId, string currentVersion, INuGetMetadataClient client)
    {
        if (KnownPackageCatalog.Find(packageId) is KnownPackageEntry entry)
        {
            return ClassifyFromCatalog(packageId, currentVersion, entry);
        }

        if (client.TryGetLatest(packageId) is NuGetPackageMetadata metadata && metadata.TargetFrameworks.Count > 0)
        {
            return ClassifyFromMetadata(packageId, currentVersion, metadata);
        }

        return new PackageCompatibility(packageId, currentVersion, Unknown,
            "Not in the built-in catalog and no NuGet metadata available; verify .NET 10 support manually.",
            ReplacementPackage: null);
    }

    private static PackageCompatibility ClassifyFromCatalog(string packageId, string currentVersion, KnownPackageEntry entry)
    {
        if (entry.Incompatible)
        {
            return new PackageCompatibility(packageId, currentVersion, Incompatible,
                entry.RecommendedAction, entry.ReplacementPackage);
        }

        // An unparseable or missing version cannot be proven >= min, so it also gets the
        // upgrade recommendation rather than a guess.
        bool provenCompatible = entry.MinCompatibleVersion is null ||
            (TryParseVersion(currentVersion, out Version current) &&
             TryParseVersion(entry.MinCompatibleVersion, out Version min) &&
             current >= min);

        if (provenCompatible)
        {
            return new PackageCompatibility(packageId, currentVersion, Compatible,
                entry.RecommendedAction, entry.ReplacementPackage);
        }

        return new PackageCompatibility(packageId, currentVersion, CompatibleWithUpgrade,
            $"Upgrade to {entry.MinCompatibleVersion} or later. {entry.RecommendedAction}",
            entry.ReplacementPackage);
    }

    private static PackageCompatibility ClassifyFromMetadata(string packageId, string currentVersion, NuGetPackageMetadata metadata)
    {
        if (!metadata.TargetFrameworks.Any(IsNet10Consumable))
        {
            return new PackageCompatibility(packageId, currentVersion, Incompatible,
                $"Latest version {metadata.LatestVersion} only targets legacy frameworks " +
                $"({string.Join(", ", metadata.TargetFrameworks)}); find a replacement.",
                ReplacementPackage: null);
        }

        if (currentVersion == metadata.LatestVersion)
        {
            return new PackageCompatibility(packageId, currentVersion, Compatible,
                $"Version {metadata.LatestVersion} targets .NET 10-consumable frameworks.",
                ReplacementPackage: null);
        }

        // Only the latest version was inspected, so the honest verdict for an older
        // pinned version is "works after upgrading to the inspected version".
        return new PackageCompatibility(packageId, currentVersion, CompatibleWithUpgrade,
            $"Upgrade to {metadata.LatestVersion}, which targets .NET 10-consumable frameworks.",
            ReplacementPackage: null);
    }

    /// <summary>
    /// True when a nuspec/csproj target framework moniker is consumable from net10.0:
    /// any netstandard, netcoreapp, or net5.0+ build.
    /// </summary>
    private static bool IsNet10Consumable(string tfm)
    {
        string normalized = tfm.Trim().ToLowerInvariant() switch
        {
            var t when t.StartsWith(".netstandard") => "netstandard" + t[".netstandard".Length..],
            var t when t.StartsWith(".netcoreapp") => "netcoreapp" + t[".netcoreapp".Length..],
            var t when t.StartsWith(".netframework") => "netframework" + t[".netframework".Length..],
            var t => t,
        };

        if (normalized.StartsWith("netstandard") || normalized.StartsWith("netcoreapp"))
        {
            return true;
        }

        if (normalized.StartsWith("netframework") || normalized.StartsWith("net4") ||
            normalized.StartsWith("net3") || normalized.StartsWith("net2"))
        {
            return false;
        }

        // "net5.0" .. "net10.0" (dotted form distinguishes modern monikers from "net45").
        return normalized.StartsWith("net") &&
               normalized.Contains('.') &&
               TryParseVersion(normalized["net".Length..], out Version version) &&
               version.Major >= 5;
    }

    /// <summary>Parses "6.0.8" or "13.0.1-beta1" (prerelease suffix ignored).</summary>
    private static bool TryParseVersion(string text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        int dash = text.IndexOfAny(['-', '+']);
        string numeric = dash < 0 ? text : text[..dash];
        if (!numeric.Contains('.'))
        {
            numeric += ".0";
        }

        return Version.TryParse(numeric, out version!);
    }
}
