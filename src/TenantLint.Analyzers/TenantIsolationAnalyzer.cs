using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TenantLint.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenantIsolationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "TL000";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "TenantLint placeholder",
        messageFormat: "TenantLint is loaded",
        category: "TenantIsolation",
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: false,
        helpLinkUri: "https://github.com/ibrahimkilic/TenantLint/blob/main/docs/rules/TL000.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
    }
}
