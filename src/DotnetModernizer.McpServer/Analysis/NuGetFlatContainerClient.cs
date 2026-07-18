using System.Text.Json;
using System.Xml.Linq;

namespace DotnetModernizer.McpServer.Analysis;

/// <summary>Target frameworks of a package's latest published version.</summary>
public sealed record NuGetPackageMetadata(
    string LatestVersion,
    IReadOnlyList<string> TargetFrameworks);

/// <summary>
/// Source of package metadata for packages not covered by <see cref="KnownPackageCatalog"/>.
/// Implementations must return null instead of throwing when the network or the package
/// is unavailable, so the analyzer can fall back to "unknown".
/// </summary>
public interface INuGetMetadataClient
{
    NuGetPackageMetadata? TryGetLatest(string packageId);
}

/// <summary>
/// Queries the NuGet V3 flat-container API: the version index for the latest version,
/// then that version's nuspec for its dependency-group target frameworks.
/// </summary>
public sealed class NuGetFlatContainerClient : INuGetMetadataClient
{
    private const string BaseUrl = "https://api.nuget.org/v3-flatcontainer/";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public NuGetPackageMetadata? TryGetLatest(string packageId)
    {
        try
        {
            string id = packageId.ToLowerInvariant();
            string indexJson = Http.GetStringAsync($"{BaseUrl}{id}/index.json").GetAwaiter().GetResult();
            string? latest = PickLatestVersion(indexJson);
            if (latest is null)
            {
                return null;
            }

            string nuspec = Http.GetStringAsync($"{BaseUrl}{id}/{latest}/{id}.nuspec").GetAwaiter().GetResult();
            return new NuGetPackageMetadata(latest, ParseTargetFrameworks(nuspec));
        }
        catch
        {
            // Offline, package not found, or malformed response — the analyzer treats
            // null as "no network data" and reports the package as unknown.
            return null;
        }
    }

    /// <summary>Latest stable version from the flat-container index; latest prerelease if no stable exists.</summary>
    private static string? PickLatestVersion(string indexJson)
    {
        using JsonDocument doc = JsonDocument.Parse(indexJson);
        if (!doc.RootElement.TryGetProperty("versions", out JsonElement versions))
        {
            return null;
        }

        // The index lists versions in ascending semver order.
        string? latestAny = null;
        string? latestStable = null;
        foreach (JsonElement version in versions.EnumerateArray())
        {
            string? v = version.GetString();
            if (v is null)
            {
                continue;
            }

            latestAny = v;
            if (!v.Contains('-'))
            {
                latestStable = v;
            }
        }

        return latestStable ?? latestAny;
    }

    private static IReadOnlyList<string> ParseTargetFrameworks(string nuspecXml)
    {
        XDocument doc = XDocument.Parse(nuspecXml);
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "group" && e.Parent?.Name.LocalName == "dependencies")
            .Select(e => e.Attribute("targetFramework")?.Value)
            .Where(tfm => !string.IsNullOrWhiteSpace(tfm))
            .Select(tfm => tfm!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
