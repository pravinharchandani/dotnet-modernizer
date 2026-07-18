using System.Xml;
using System.Xml.Linq;
using DotnetModernizer.McpServer.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotnetModernizer.McpServer.Analysis;

/// <summary>
/// Finds uses of APIs from <see cref="BreakingApiCatalog"/> in a project's C# sources.
/// </summary>
/// <remarks>
/// Detection is two-level by design. The analyzed projects are legacy net472 code that
/// generally cannot be compiled on this machine (framework reference assemblies and
/// NuGet packages like System.Web.Mvc are absent), so a Roslyn compilation built from
/// the runtime's own assemblies resolves only part of the symbols:
///  1. Semantic level (preferred): when the semantic model resolves a symbol, we match
///     its full metadata name against the catalog — precise, alias- and using-agnostic.
///  2. Syntactic fallback: when a symbol cannot be resolved (its assembly is missing),
///     we match dotted qualified names and using-directives from the syntax tree,
///     expanding using-aliases first, so "using SWM = System.Web.Mvc;" plus
///     "SWM.Controller" is still caught.
/// Both levels operate on syntax/symbol nodes only — never on raw source text — so
/// string literals and comments can never produce findings.
/// </remarks>
public static class BreakingApiAnalyzer
{
    private static readonly string[] ValidFilters = ["all", BreakingApiCatalog.SeverityBlocker, BreakingApiCatalog.SeverityWarning];

    private const string Semantic = "semantic";
    private const string Syntactic = "syntactic";

    /// <summary>References from the running runtime; enough to resolve BCL symbols of legacy code.</summary>
    private static readonly Lazy<IReadOnlyList<MetadataReference>> RuntimeReferences = new(() =>
        ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .ToList());

    public static BreakingApiScanResult Analyze(string projectPath, string severityFilter = "all")
    {
        severityFilter = string.IsNullOrWhiteSpace(severityFilter) ? "all" : severityFilter.Trim().ToLowerInvariant();
        if (!ValidFilters.Contains(severityFilter))
        {
            return Failure(projectPath ?? string.Empty,
                $"Invalid severityFilter '{severityFilter}': expected \"all\", \"blocker\" or \"warning\".");
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return Failure(projectPath ?? string.Empty, "projectPath must be a non-empty absolute path to a .csproj file.");
        }

        if (!File.Exists(projectPath))
        {
            return Failure(projectPath, $"Project file not found: {projectPath}");
        }

        // The csproj content is not needed for source discovery, but a malformed project
        // file must surface as an explicit error rather than a silent partial scan.
        try
        {
            XDocument.Load(projectPath);
        }
        catch (XmlException ex)
        {
            return Failure(projectPath, $"Malformed csproj XML: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure(projectPath, $"Could not read csproj: {ex.Message}");
        }

        string projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        List<string> sourceFiles;
        try
        {
            sourceFiles = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsInBuildOutput(f, projectDir))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure(projectPath, $"Could not enumerate sources: {ex.Message}");
        }

        var trees = sourceFiles
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "BreakingApiAnalysis",
            trees,
            RuntimeReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var findings = new List<BreakingApiFinding>();
        foreach (SyntaxTree tree in trees)
        {
            CollectFindings(tree, compilation.GetSemanticModel(tree), findings);
        }

        findings = findings
            .DistinctBy(f => (f.FilePath, f.Line, f.ApiName))
            .Where(f => severityFilter == "all" || f.Severity == severityFilter)
            .OrderBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Line)
            .ToList();

        var summary = new BreakingApiSummary(
            sourceFiles.Count,
            findings.Count(f => f.Severity == BreakingApiCatalog.SeverityBlocker),
            findings.Count(f => f.Severity == BreakingApiCatalog.SeverityWarning));

        return new BreakingApiScanResult(projectPath, findings, summary);
    }

    private static BreakingApiScanResult Failure(string projectPath, string error) =>
        new(projectPath, [], new BreakingApiSummary(0, 0, 0), error);

    private static bool IsInBuildOutput(string file, string projectDir)
    {
        string relative = Path.GetRelativePath(projectDir, file);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static void CollectFindings(SyntaxTree tree, SemanticModel model, List<BreakingApiFinding> findings)
    {
        SyntaxNode root = tree.GetRoot();
        Dictionary<string, string> aliases = CollectAliases(root);

        foreach (SyntaxNode node in root.DescendantNodes())
        {
            switch (node)
            {
                case UsingDirectiveSyntax usingDirective when usingDirective.Name is not null:
                    CheckUsingDirective(usingDirective, model, aliases, findings);
                    break;

                case AttributeSyntax attribute:
                    CheckAttribute(attribute, model, aliases, findings);
                    break;

                // Only the top-most node of a dotted chain is checked, so "System.Web.Mvc.Controller"
                // yields one finding rather than one per segment.
                case ExpressionSyntax expression when IsNameLike(expression) &&
                                                      !IsPartOfLargerChain(expression) &&
                                                      !IsInsideUsingOrAttributeName(expression):
                    CheckNameChain(expression, model, aliases, findings);
                    break;
            }
        }
    }

    private static Dictionary<string, string> CollectAliases(SyntaxNode root)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (UsingDirectiveSyntax u in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            if (u.Alias is not null && u.Name is not null && Flatten(u.Name, aliases: null) is string target)
            {
                aliases[u.Alias.Name.Identifier.Text] = target;
            }
        }

        return aliases;
    }

    private static void CheckUsingDirective(
        UsingDirectiveSyntax usingDirective,
        SemanticModel model,
        Dictionary<string, string> aliases,
        List<BreakingApiFinding> findings)
    {
        NameSyntax name = usingDirective.Name!;
        ISymbol? symbol = model.GetSymbolInfo(name).Symbol;

        if (symbol is INamedTypeSymbol type)
        {
            // "using static Banned.Type;" or "using X = Banned.Type;"
            AddTypeMatches(type, name, Semantic, findings);
            return;
        }

        if (symbol is INamespaceSymbol ns)
        {
            AddNamespaceRuleMatches(ns.ToDisplayString(), ns.ToDisplayString(), name, Semantic, findings);
            return;
        }

        if (Flatten(name, aliases: null) is not string text)
        {
            return;
        }

        // Unresolved: the namespace's assembly is missing from this runtime, which for
        // legacy code is itself a signal. Match the dotted text against namespace rules,
        // exact type rules (alias to a type), and type rules whose namespace this is
        // (e.g. "using System.Runtime.Serialization.Formatters.Binary;" implies BinaryFormatter usage).
        AddNamespaceRuleMatches(text, text, name, Syntactic, findings);
        foreach (BreakingApiRule rule in BreakingApiCatalog.Rules.Where(r => r.Kind == BreakingApiMatchKind.Type))
        {
            if (text == rule.FullName || rule.FullName.StartsWith(text + ".", StringComparison.Ordinal))
            {
                AddFinding(findings, name, rule.FullName, rule, Syntactic);
            }
        }
    }

    private static void CheckAttribute(
        AttributeSyntax attribute,
        SemanticModel model,
        Dictionary<string, string> aliases,
        List<BreakingApiFinding> findings)
    {
        if (model.GetSymbolInfo(attribute).Symbol is IMethodSymbol ctor)
        {
            AddTypeMatches(ctor.ContainingType, attribute.Name, Semantic, findings);
            return;
        }

        if (Flatten(attribute.Name, aliases) is not string text)
        {
            return;
        }

        // Unresolved attribute: expand through the file's using directives and the
        // optional "Attribute" suffix, but only against exact type rules — never
        // namespace prefixes — so an unrelated unresolved attribute cannot be
        // misattributed to a banned namespace.
        List<string> namespacePrefixes = attribute.SyntaxTree.GetRoot()
            .DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(u => u.Alias is null && u.StaticKeyword == default && u.Name is not null)
            .Select(u => Flatten(u.Name!, aliases: null))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        IEnumerable<string> candidates = new[] { text, text + "Attribute" }
            .SelectMany(n => namespacePrefixes.Select(p => p + "." + n).Append(n));

        foreach (string candidate in candidates.Distinct())
        {
            foreach (BreakingApiRule rule in BreakingApiCatalog.Rules.Where(r => r.Kind == BreakingApiMatchKind.Type))
            {
                if (candidate == rule.FullName)
                {
                    AddFinding(findings, attribute.Name, rule.FullName, rule, Syntactic);
                }
            }
        }
    }

    private static void CheckNameChain(
        ExpressionSyntax node,
        SemanticModel model,
        Dictionary<string, string> aliases,
        List<BreakingApiFinding> findings)
    {
        ISymbol? symbol = model.GetSymbolInfo(node).Symbol;
        if (symbol is not null)
        {
            CheckResolvedSymbol(symbol, node, findings);
            return;
        }

        if (Flatten(node, aliases) is not string text)
        {
            return;
        }

        AddNamespaceRuleMatches(text, text, node, Syntactic, findings);
        foreach (BreakingApiRule rule in BreakingApiCatalog.Rules)
        {
            bool matches = rule.Kind switch
            {
                // A type or member match also fires for a longer chain rooted at it,
                // e.g. an aliased "BF.Deserialize" expanding to "...BinaryFormatter.Deserialize".
                BreakingApiMatchKind.Type or BreakingApiMatchKind.Member =>
                    text == rule.FullName || text.StartsWith(rule.FullName + ".", StringComparison.Ordinal),
                _ => false,
            };

            if (matches)
            {
                AddFinding(findings, node, text, rule, Syntactic);
            }
        }
    }

    private static void CheckResolvedSymbol(ISymbol symbol, SyntaxNode node, List<BreakingApiFinding> findings)
    {
        switch (symbol)
        {
            case INamedTypeSymbol type:
                AddTypeMatches(type, node, Semantic, findings);
                break;

            case IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol when symbol.ContainingType is not null:
                INamedTypeSymbol containing = symbol.ContainingType;
                string memberFullName = TypeFullName(containing) + "." + symbol.Name;

                foreach (BreakingApiRule rule in BreakingApiCatalog.Rules.Where(r => r.Kind == BreakingApiMatchKind.Member))
                {
                    if (memberFullName == rule.FullName)
                    {
                        AddFinding(findings, node, memberFullName, rule, Semantic);
                    }
                }

                AddTypeMatches(containing, node, Semantic, findings);
                break;

            // Namespace symbols are skipped: the concrete type/member usage produces the
            // finding, and skipping them keeps "System.Web.HttpUtility.HtmlEncode(...)"
            // from being flagged through its "System.Web" namespace segment.
        }
    }

    private static void AddTypeMatches(INamedTypeSymbol type, SyntaxNode node, string level, List<BreakingApiFinding> findings)
    {
        string fullName = TypeFullName(type.OriginalDefinition);

        foreach (BreakingApiRule rule in BreakingApiCatalog.Rules)
        {
            bool matches = rule.Kind switch
            {
                BreakingApiMatchKind.Type => fullName == rule.FullName,
                BreakingApiMatchKind.Namespace => MatchesNamespaceRule(fullName, rule),
                _ => false,
            };

            if (matches)
            {
                AddFinding(findings, node, fullName, rule, level);
            }
        }
    }

    private static void AddNamespaceRuleMatches(
        string dottedName, string apiName, SyntaxNode node, string level, List<BreakingApiFinding> findings)
    {
        foreach (BreakingApiRule rule in BreakingApiCatalog.Rules.Where(r => r.Kind == BreakingApiMatchKind.Namespace))
        {
            if (MatchesNamespaceRule(dottedName, rule))
            {
                AddFinding(findings, node, apiName, rule, level);
            }
        }
    }

    private static bool MatchesNamespaceRule(string dottedName, BreakingApiRule rule)
    {
        if (dottedName != rule.FullName && !dottedName.StartsWith(rule.FullName + ".", StringComparison.Ordinal))
        {
            return false;
        }

        return rule.ExemptTypes is null || !rule.ExemptTypes.Any(exempt =>
            dottedName == exempt || dottedName.StartsWith(exempt + ".", StringComparison.Ordinal));
    }

    private static void AddFinding(
        List<BreakingApiFinding> findings, SyntaxNode node, string apiName, BreakingApiRule rule, string level)
    {
        FileLinePositionSpan span = node.GetLocation().GetLineSpan();
        findings.Add(new BreakingApiFinding(
            span.Path,
            span.StartLinePosition.Line + 1,
            apiName,
            rule.Severity,
            rule.Recommendation,
            level));
    }

    private static string TypeFullName(INamedTypeSymbol type)
    {
        string name = type.Name;
        for (INamedTypeSymbol? outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            name = outer.Name + "." + name;
        }

        INamespaceSymbol? ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? name : ns.ToDisplayString() + "." + name;
    }

    private static bool IsNameLike(ExpressionSyntax node) =>
        node is IdentifierNameSyntax or GenericNameSyntax or QualifiedNameSyntax
            or AliasQualifiedNameSyntax or MemberAccessExpressionSyntax;

    /// <summary>True when the node is a segment of an enclosing dotted name or member access.</summary>
    private static bool IsPartOfLargerChain(ExpressionSyntax node) =>
        node.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or MemberAccessExpressionSyntax;

    private static bool IsInsideUsingOrAttributeName(ExpressionSyntax node) =>
        node.Ancestors().Any(a => a is UsingDirectiveSyntax ||
                                  (a is AttributeSyntax attr && attr.Name.Span.Contains(node.Span)));

    /// <summary>
    /// Rebuilds the dotted text of a pure name/member-access chain ("SWM.Controller",
    /// "System.Web.Caching.Cache"). Returns null when the chain contains anything that is
    /// not a simple name (invocations, indexers, literals), which also guarantees string
    /// literals can never enter name matching. When <paramref name="aliases"/> is given,
    /// a leading using-alias segment is expanded to its target.
    /// </summary>
    private static string? Flatten(SyntaxNode node, Dictionary<string, string>? aliases)
    {
        string? text = node switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            QualifiedNameSyntax qualified =>
                Flatten(qualified.Left, aliases: null) is string left && Flatten(qualified.Right, aliases: null) is string right
                    ? left + "." + right
                    : null,
            MemberAccessExpressionSyntax member =>
                Flatten(member.Expression, aliases: null) is string expr && Flatten(member.Name, aliases: null) is string name
                    ? expr + "." + name
                    : null,
            AliasQualifiedNameSyntax aliasQualified =>
                // "global::A.B" — the global alias is a no-op prefix.
                aliasQualified.Alias.Identifier.Text == "global"
                    ? Flatten(aliasQualified.Name, aliases: null)
                    : aliasQualified.Alias.Identifier.Text + "." + Flatten(aliasQualified.Name, aliases: null),
            _ => null,
        };

        if (text is null || aliases is null || aliases.Count == 0)
        {
            return text;
        }

        int firstDot = text.IndexOf('.');
        string head = firstDot < 0 ? text : text[..firstDot];
        if (aliases.TryGetValue(head, out string? target))
        {
            return firstDot < 0 ? target : target + text[firstDot..];
        }

        return text;
    }
}
