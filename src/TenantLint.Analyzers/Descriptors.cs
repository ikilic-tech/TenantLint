using Microsoft.CodeAnalysis;

namespace TenantLint.Analyzers;

internal static class Descriptors
{
    private const string Category = "TenantIsolation";
    private const string HelpLinkBase = "https://github.com/ikilic-tech/TenantLint/blob/main/docs/rules/";

    public static readonly DiagnosticDescriptor TL000 = new(
        id: "TL000",
        title: "TenantLint placeholder",
        messageFormat: "TenantLint is loaded",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: false,
        helpLinkUri: HelpLinkBase + "TL000.md");

    public static readonly DiagnosticDescriptor TL900 = new(
        id: "TL900",
        title: "Dynamic query filter detected",
        messageFormat: "Dynamic query filter detected in '{0}'; TL005 disabled for this context",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL900.md",
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });
}
