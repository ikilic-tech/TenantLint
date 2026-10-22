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
        messageFormat: "Dynamic query filter detected in '{0}'; TL005 reduced to Info for this context",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL900.md",
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    public static readonly DiagnosticDescriptor TL901 = new(
        id: "TL901",
        title: "CrossTenant attribute should state a reason",
        messageFormat: "[CrossTenant] on '{0}' should provide a reason: [CrossTenant(\"reason\")]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL901.md");

    public static readonly DiagnosticDescriptor TL902 = new(
        id: "TL902",
        title: "Class-level CrossTenant suppresses all methods",
        messageFormat: "Class-level [CrossTenant] on '{0}' suppresses tenant checks for all methods; prefer method-level [CrossTenant(\"reason\")]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLinkBase + "TL902.md");
}
