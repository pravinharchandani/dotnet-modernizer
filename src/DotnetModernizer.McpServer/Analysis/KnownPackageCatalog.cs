namespace DotnetModernizer.McpServer.Analysis;

/// <summary>
/// Compatibility facts for one well-known package. <see cref="Incompatible"/> means the
/// package can never run on .NET 10; otherwise <see cref="MinCompatibleVersion"/> is the
/// first version usable from .NET 10 (a netstandard2.0/2.1 or net5.0+ build), and older
/// versions get status "compatible-with-upgrade".
/// </summary>
public sealed record KnownPackageEntry(
    string PackageId,
    bool Incompatible,
    string? MinCompatibleVersion,
    string RecommendedAction,
    string? ReplacementPackage = null);

/// <summary>
/// Built-in compatibility data for the common legacy-package suspects, so the analyzer
/// gives firm answers for them without network access. Pure data: new packages are added
/// as rows, not code.
/// </summary>
public static class KnownPackageCatalog
{
    private static readonly IReadOnlyList<KnownPackageEntry> Entries =
    [
        // ASP.NET (System.Web-based): nothing to upgrade to, the framework itself is gone.
        new("Microsoft.AspNet.Mvc", Incompatible: true, MinCompatibleVersion: null,
            "ASP.NET MVC 5 does not run on .NET 10; rewrite the web layer on ASP.NET Core MVC."),
        new("Microsoft.AspNet.Razor", Incompatible: true, null,
            "System.Web.Razor does not run on .NET 10; ASP.NET Core includes its own Razor engine."),
        new("Microsoft.AspNet.WebPages", Incompatible: true, null,
            "ASP.NET Web Pages does not run on .NET 10; rewrite on ASP.NET Core Razor Pages."),
        new("Microsoft.AspNet.WebApi.Core", Incompatible: true, null,
            "ASP.NET Web API 2 does not run on .NET 10; migrate controllers to ASP.NET Core."),
        new("Microsoft.AspNet.WebApi.WebHost", Incompatible: true, null,
            "ASP.NET Web API 2 hosting does not run on .NET 10; migrate to ASP.NET Core."),

        new("EntityFramework", Incompatible: false, MinCompatibleVersion: "6.3.0",
            "EF6 runs on modern .NET from 6.3, but EF Core 10 is the recommended target.",
            ReplacementPackage: "Microsoft.EntityFrameworkCore"),

        new("Newtonsoft.Json", Incompatible: false, "13.0.1",
            "Compatible with .NET 10; consider System.Text.Json as the modern default serializer."),

        // WCF: client stacks ship as System.ServiceModel.* packages; server side needs CoreWCF.
        new("System.ServiceModel.Primitives", Incompatible: false, "4.10.0",
            "WCF client works on .NET 10; WCF server hosting requires CoreWCF.",
            ReplacementPackage: "CoreWCF.Primitives"),
        new("System.ServiceModel.Http", Incompatible: false, "4.10.0",
            "WCF client works on .NET 10; WCF server hosting requires CoreWCF.",
            ReplacementPackage: "CoreWCF.Http"),
        new("System.ServiceModel.NetTcp", Incompatible: false, "4.10.0",
            "WCF client works on .NET 10; WCF server hosting requires CoreWCF.",
            ReplacementPackage: "CoreWCF.NetTcp"),
        new("System.ServiceModel.Duplex", Incompatible: false, "4.10.0",
            "WCF client works on .NET 10; WCF server hosting requires CoreWCF."),
        new("System.ServiceModel.Security", Incompatible: false, "4.10.0",
            "WCF client works on .NET 10; WCF server hosting requires CoreWCF."),

        // Framework-era DI containers.
        new("Unity", Incompatible: false, "5.0.0",
            "Unity container runs on .NET 10 from 5.x but is unmaintained; prefer Microsoft.Extensions.DependencyInjection.",
            ReplacementPackage: "Microsoft.Extensions.DependencyInjection"),
        new("Ninject", Incompatible: false, "3.3.0",
            "Ninject runs on .NET 10 from 3.3 but is in maintenance mode; prefer Microsoft.Extensions.DependencyInjection.",
            ReplacementPackage: "Microsoft.Extensions.DependencyInjection"),
        new("StructureMap", Incompatible: true, null,
            "StructureMap is discontinued; migrate to its successor Lamar or Microsoft.Extensions.DependencyInjection.",
            ReplacementPackage: "Lamar"),
        new("Autofac", Incompatible: false, "4.0.0",
            "Compatible with .NET 10 from 4.x; integrates with Microsoft.Extensions.DependencyInjection."),

        // Logging.
        new("log4net", Incompatible: false, "2.0.8",
            "log4net runs on .NET 10 from 2.0.8; consider Microsoft.Extensions.Logging as the modern default.",
            ReplacementPackage: "Microsoft.Extensions.Logging"),
        new("NLog", Incompatible: false, "4.5.0",
            "Compatible with .NET 10 from 4.5; integrates with Microsoft.Extensions.Logging."),
        new("Serilog", Incompatible: false, "2.0.0",
            "Compatible with .NET 10; integrates with Microsoft.Extensions.Logging."),
    ];

    private static readonly Dictionary<string, KnownPackageEntry> ById =
        Entries.ToDictionary(e => e.PackageId, StringComparer.OrdinalIgnoreCase);

    public static KnownPackageEntry? Find(string packageId) =>
        ById.TryGetValue(packageId, out KnownPackageEntry? entry) ? entry : null;
}
