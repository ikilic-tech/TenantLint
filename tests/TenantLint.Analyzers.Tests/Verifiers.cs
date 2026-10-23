using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace TenantLint.Analyzers.Tests;

public static class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    // Diagnostics that are disabled by default (isEnabledByDefault: false).
    // The Roslyn test framework does not automatically suppress these,
    // so we exclude them in helpers that don't provide an .editorconfig.
    // Tests that need these diagnostics should use the *AndOptions* helpers
    // with an .editorconfig that explicitly enables them.
    internal static readonly string[] DisabledByDefaultIds = { "TL111" };

    public static readonly ReferenceAssemblies Net80WithEfCore =
        ReferenceAssemblies.Net.Net80.AddPackages(
            ImmutableArray.Create(
                new PackageIdentity("Microsoft.EntityFrameworkCore", "8.0.8"),
                new PackageIdentity("Microsoft.EntityFrameworkCore.Relational", "8.0.8")));

    public static readonly ReferenceAssemblies Net100WithEfCore10 =
        ReferenceAssemblies.Net.Net100.AddPackages(
            ImmutableArray.Create(
                new PackageIdentity("Microsoft.EntityFrameworkCore", "10.0.0"),
                new PackageIdentity("Microsoft.EntityFrameworkCore.Relational", "10.0.0")));

    public static readonly ReferenceAssemblies Net80WithEfCoreAndDapper =
        Net80WithEfCore.AddPackages(
            ImmutableArray.Create(
                new PackageIdentity("Dapper", "2.1.35")));

    public static readonly ReferenceAssemblies Net100WithEfCore10AndDapper =
        Net100WithEfCore10.AddPackages(
            ImmutableArray.Create(
                new PackageIdentity("Dapper", "2.1.35")));

    public static DiagnosticResult Diagnostic(string diagnosticId)
        => CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
        => CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(descriptor);

    public static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        test.DisabledDiagnostics.AddRange(DisabledByDefaultIds);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }

    public static async Task VerifyWithEfCoreAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net80WithEfCore,
        };
        test.DisabledDiagnostics.AddRange(DisabledByDefaultIds);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }

    public static async Task VerifyWithEfCore10Async(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net100WithEfCore10,
        };
        test.DisabledDiagnostics.AddRange(DisabledByDefaultIds);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }

    public static async Task VerifyWithDapperAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net80WithEfCoreAndDapper,
        };
        test.DisabledDiagnostics.AddRange(DisabledByDefaultIds);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }

    public static async Task VerifyWithDapper10Async(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net100WithEfCore10AndDapper,
        };
        test.DisabledDiagnostics.AddRange(DisabledByDefaultIds);
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }

    public static async Task VerifyWithEfCoreAndOptionsAsync(
        string source,
        string editorconfigContent,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net80WithEfCore,
        };
        test.TestState.AnalyzerConfigFiles.Add(
            ("/.editorconfig", editorconfigContent));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }

    public static async Task VerifyWithEfCore10AndOptionsAsync(
        string source,
        string editorconfigContent,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = Net100WithEfCore10,
        };
        test.TestState.AnalyzerConfigFiles.Add(
            ("/.editorconfig", editorconfigContent));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }
}

public static class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public static DiagnosticResult Diagnostic(string diagnosticId)
        => CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static DiagnosticResult Diagnostic(DiagnosticDescriptor descriptor)
        => CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(descriptor);

    public static async Task VerifyCodeFixAsync(
        string source,
        DiagnosticResult[] expected,
        string fixedSource,
        ReferenceAssemblies? referenceAssemblies = null,
        int codeActionIndex = 0,
        string? equivalenceKey = null,
        DiagnosticResult[]? remainingDiagnostics = null)
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = referenceAssemblies ?? CSharpAnalyzerVerifier<TAnalyzer>.Net80WithEfCore,
            CodeActionIndex = codeActionIndex,
            CodeActionEquivalenceKey = equivalenceKey,
        };
        test.DisabledDiagnostics.AddRange(CSharpAnalyzerVerifier<TAnalyzer>.DisabledByDefaultIds);
        test.ExpectedDiagnostics.AddRange(expected);
        if (remainingDiagnostics is not null)
            test.FixedState.ExpectedDiagnostics.AddRange(remainingDiagnostics);
        await test.RunAsync(CancellationToken.None);
    }

    public static async Task VerifyCodeFixWithOptionsAsync(
        string source,
        DiagnosticResult[] expected,
        string fixedSource,
        string editorconfigContent,
        ReferenceAssemblies? referenceAssemblies = null,
        int codeActionIndex = 0,
        string? equivalenceKey = null)
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = referenceAssemblies ?? CSharpAnalyzerVerifier<TAnalyzer>.Net80WithEfCore,
            CodeActionIndex = codeActionIndex,
            CodeActionEquivalenceKey = equivalenceKey,
        };
        test.TestState.AnalyzerConfigFiles.Add(
            ("/.editorconfig", editorconfigContent));
        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync(CancellationToken.None);
    }
}
