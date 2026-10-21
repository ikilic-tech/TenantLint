using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace TenantLint.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(TenantIsolationCodeFixProvider))]
public sealed class TenantIsolationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray<string>.Empty;

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context) => Task.CompletedTask;
}
