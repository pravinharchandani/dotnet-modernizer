namespace DotnetModernizer.McpServer.Analysis;

/// <summary>How a <see cref="BreakingApiRule"/> is matched against a symbol or name.</summary>
public enum BreakingApiMatchKind
{
    /// <summary>Matches every type under the namespace (prefix match), minus <see cref="BreakingApiRule.ExemptTypes"/>.</summary>
    Namespace,

    /// <summary>Matches one specific type by full metadata name.</summary>
    Type,

    /// <summary>Matches one specific member, written as "Full.Type.Name.MemberName".</summary>
    Member,
}

public sealed record BreakingApiRule(
    BreakingApiMatchKind Kind,
    string FullName,
    string Severity,
    string Recommendation,
    IReadOnlyList<string>? ExemptTypes = null);

/// <summary>
/// Catalog of .NET Framework APIs that are removed or fundamentally changed on .NET 10.
/// Pure data so new incompatibilities are added as rows, not detection code.
/// </summary>
public static class BreakingApiCatalog
{
    public const string SeverityBlocker = "blocker";
    public const string SeverityWarning = "warning";

    public static readonly IReadOnlyList<BreakingApiRule> Rules =
    [
        new(BreakingApiMatchKind.Namespace, "System.Web",
            SeverityBlocker, "ASP.NET Framework; migrate to ASP.NET Core",
            ExemptTypes: ["System.Web.HttpUtility"]),

        new(BreakingApiMatchKind.Type, "System.ServiceModel.ServiceContractAttribute",
            SeverityBlocker, "WCF server; migrate to CoreWCF or gRPC/REST"),

        new(BreakingApiMatchKind.Type, "System.ServiceModel.OperationContractAttribute",
            SeverityBlocker, "WCF server; migrate to CoreWCF or gRPC/REST"),

        new(BreakingApiMatchKind.Type, "System.Runtime.Serialization.Formatters.Binary.BinaryFormatter",
            SeverityBlocker, "Removed for security; migrate to System.Text.Json"),

        new(BreakingApiMatchKind.Namespace, "System.Runtime.Remoting",
            SeverityBlocker, "Removed; migrate to IPC/gRPC"),

        new(BreakingApiMatchKind.Namespace, "System.EnterpriseServices",
            SeverityBlocker, "Removed; COM+ services have no .NET 10 equivalent, rearchitect"),

        new(BreakingApiMatchKind.Member, "System.AppDomain.CreateDomain",
            SeverityWarning, "Single AppDomain in modern .NET; use AssemblyLoadContext"),

        new(BreakingApiMatchKind.Namespace, "Microsoft.VisualBasic",
            SeverityWarning, "VB runtime helpers from C#; prefer BCL equivalents"),
    ];
}
