using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TenantLint.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenantIsolationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.TL000, Descriptors.TL900, Descriptors.TL901, Descriptors.TL902);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationStartContext =>
        {
            var options = ConfigOptions.Create(
                compilationStartContext.Options.AnalyzerConfigOptionsProvider,
                compilationStartContext.Compilation);

            var builder = new TenantModelBuilder(compilationStartContext.Compilation, options);
            var model = builder.Build();

            if (!model.HasEfCore)
                return;

            // Register a symbol action to satisfy RS1013 and prepare for future rules.
            // Future milestones (M2+) will add real analysis here.
            compilationStartContext.RegisterSymbolAction(
                _ => { },
                SymbolKind.NamedType);

            // Report TL900 for each DbContext with dynamic filters
            if (model.DynamicFilterContexts.Count > 0)
            {
                compilationStartContext.RegisterCompilationEndAction(endContext =>
                {
                    foreach (var contextType in model.DynamicFilterContexts)
                    {
                        var location = contextType.Locations.FirstOrDefault() ?? Location.None;
                        endContext.ReportDiagnostic(Diagnostic.Create(
                            Descriptors.TL900,
                            location,
                            contextType.Name));
                    }
                });
            }
        });
    }
}
