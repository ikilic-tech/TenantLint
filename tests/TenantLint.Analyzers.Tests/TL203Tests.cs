using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL203Tests
{
    private static CSharpAnalyzerTest<TenantIsolationAnalyzer, DefaultVerifier> CreateTestWithNoWarn(
        string source,
        params string[] suppressedIds)
    {
        var options = ImmutableDictionary.CreateBuilder<string, ReportDiagnostic>();
        foreach (var id in suppressedIds)
            options.Add(id, ReportDiagnostic.Suppress);

        var test = new CSharpAnalyzerTest<TenantIsolationAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        test.DisabledDiagnostics.AddRange(CSharpAnalyzerVerifier<TenantIsolationAnalyzer>.DisabledByDefaultIds);

        var suppressionOptions = options.ToImmutable();
        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var project = solution.GetProject(projectId)!;
            var compilationOptions = (CSharpCompilationOptions)project.CompilationOptions!;
            compilationOptions = compilationOptions.WithSpecificDiagnosticOptions(suppressionOptions);
            return solution.WithProjectCompilationOptions(projectId, compilationOptions);
        });

        return test;
    }

    // ===========================================================
    // Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task NoWarn_TL001_Reports()
    {
        var source = @"
class C
{
    void M() { }
}
";
        var test = CreateTestWithNoWarn(source, "TL001");

        var expected = Verify.Diagnostic(Descriptors.TL203)
            .WithArguments("TL001");

        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NoWarn_TL005_Reports()
    {
        var source = @"
class C
{
    void M() { }
}
";
        var test = CreateTestWithNoWarn(source, "TL005");

        var expected = Verify.Diagnostic(Descriptors.TL203)
            .WithArguments("TL005");

        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NoWarn_MultipleTlRules_ReportsBoth()
    {
        var source = @"
class C
{
    void M() { }
}
";
        var test = CreateTestWithNoWarn(source, "TL001", "TL005");

        var expected = Verify.Diagnostic(Descriptors.TL203)
            .WithArguments("TL001, TL005");

        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync(CancellationToken.None);
    }

    // ===========================================================
    // Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task NoTlRulesSuppressed_NoReport()
    {
        var source = @"
class C
{
    void M() { }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task TL203_ItselfSuppressed_NoReport()
    {
        var source = @"
class C
{
    void M() { }
}
";
        var test = CreateTestWithNoWarn(source, "TL203");
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NonTlRuleSuppressed_NoReport()
    {
        var source = @"
class C
{
    void M() { }
}
";
        var test = CreateTestWithNoWarn(source, "CS1234");
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TlRuleSuggestionSeverity_NoReport()
    {
        var source = @"
class C
{
    void M() { }
}
";
        var editorconfig = @"root = true

[*.cs]
dotnet_diagnostic.TL001.severity = suggestion
";
        var test = new CSharpAnalyzerTest<TenantIsolationAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", editorconfig));
        test.DisabledDiagnostics.AddRange(CSharpAnalyzerVerifier<TenantIsolationAnalyzer>.DisabledByDefaultIds);

        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PragmaDisable_NotGlobal_NoReport()
    {
        // #pragma is handled by TL201, not TL203
        var source = @"
class C
{
    void M()
    {
#pragma warning disable TL001 // Admin view
        var x = 1;
#pragma warning restore TL001
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }
}
