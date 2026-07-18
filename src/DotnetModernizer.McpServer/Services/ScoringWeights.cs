namespace DotnetModernizer.McpServer.Services;

/// <summary>
/// All numeric inputs of the effort scoring model, kept as pure data so the model can be
/// tuned without touching estimation code.
///
/// Rationale per weight:
///
/// - Base score by project kind (classlib 1, console 2, web 8, wcf 10): a class library
///   or console app is mostly a port — retarget the TFM, fix API fallout. A System.Web
///   site or a WCF service is a rewrite of the hosting model itself (ASP.NET Core /
///   CoreWCF or gRPC): routing, pipeline, configuration and startup all change even if
///   the business code moves over unchanged. WCF scores above web because the contract
///   and binding model has no first-party successor. Test projects score like class
///   libraries (the test framework migration is routine); unknown kinds get the
///   conservative class-library base because nothing hosting-specific was detected.
///
/// - +3 per distinct blocker API family: a family (one catalog rule) is one migration
///   problem to solve, however many call sites it has — 40 usages of System.Web.Mvc are
///   one problem, not forty. Call-site count is porting typing effort, which the base
///   score already covers.
///
/// - +1 per warning-severity family: warnings have a known mechanical replacement
///   (AssemblyLoadContext, BCL equivalents), so they cost a third of a blocker.
///
/// - +2 per incompatible NuGet package: each one forces finding and adopting a
///   replacement library, an open-ended task. +0.5 per compatible-with-upgrade package:
///   a version bump plus fixing whatever the newer major version broke.
///
/// - Dependency factor 1 + 0.1 × dependents: churn in a widely-referenced project
///   ripples into every dependent's build and tests, so the same nominal work is
///   riskier and needs more coordination.
///
/// - +1 flat for legacy-style csproj: the SDK-style conversion is real but bounded
///   work, and it is flat (not multiplied) because conversion effort does not grow
///   with how many projects reference this one.
///
/// - Tiers: ≤ 4 is a quick-win (a day-ish: retarget, convert, done), ≤ 12 is moderate
///   (real API/package work but no hosting rewrite), above that is complex (hosting
///   model rewrite or several stacked problems).
/// </summary>
public static class ScoringWeights
{
    public const double ClasslibBase = 1;
    public const double ConsoleBase = 2;
    public const double WebBase = 8;
    public const double WcfBase = 10;

    public const double BlockerFamilyPoints = 3;
    public const double WarningFamilyPoints = 1;

    public const double IncompatiblePackagePoints = 2;
    public const double UpgradePackagePoints = 0.5;

    public const double DependentFactorStep = 0.1;
    public const double LegacyCsprojPoints = 1;

    public const double QuickWinMaxScore = 4;
    public const double ModerateMaxScore = 12;

    public static double BaseFor(string projectKind) => projectKind switch
    {
        "console" => ConsoleBase,
        "web" => WebBase,
        "wcf" => WcfBase,
        _ => ClasslibBase, // classlib, test, unknown
    };
}
