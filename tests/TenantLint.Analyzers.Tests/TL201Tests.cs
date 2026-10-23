using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL201Tests
{
    /// <summary>
    /// Creates a test instance that does NOT suppress TL201 in DisabledDiagnostics,
    /// unlike the standard helpers. This is needed because TL201 IS the rule under test.
    /// </summary>
    private static CSharpAnalyzerTest<TenantIsolationAnalyzer, DefaultVerifier> CreateTL201Test(
        string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TenantIsolationAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            TestBehaviors = TestBehaviors.SkipSuppressionCheck,
        };
        // Only suppress TL111 (disabled-by-default), not TL201 — we need TL201 active
        // since it's the rule under test.
        test.DisabledDiagnostics.Add("TL111");
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    // ===========================================================
    // Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task PragmaDisableTL001_NoComment_Reports()
    {
        var source = @"
class C
{
    void M()
    {
{|#0:#pragma warning disable TL001|}
        var x = 1;
#pragma warning restore TL001
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL201)
            .WithLocation(0)
            .WithArguments("TL001");

        var test = CreateTL201Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PragmaDisableTL001_TodoComment_Reports()
    {
        var source = @"
class C
{
    void M()
    {
{|#0:#pragma warning disable TL001 // TODO|}
        var x = 1;
#pragma warning restore TL001
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL201)
            .WithLocation(0)
            .WithArguments("TL001");

        var test = CreateTL201Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PragmaDisableTL001_NaComment_Reports()
    {
        var source = @"
class C
{
    void M()
    {
{|#0:#pragma warning disable TL001 // n/a|}
        var x = 1;
#pragma warning restore TL001
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL201)
            .WithLocation(0)
            .WithArguments("TL001");

        var test = CreateTL201Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    // ===========================================================
    // Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task PragmaDisableTL001_WithComment_NoReport()
    {
        var source = @"
class C
{
    void M()
    {
#pragma warning disable TL001 // Cross-tenant admin view
        var x = 1;
#pragma warning restore TL001
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task PragmaDisableCS1234_NonTlRule_NoReport()
    {
        var source = @"
class C
{
    void M()
    {
#pragma warning disable CS1234
        var x = 1;
#pragma warning restore CS1234
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task PragmaRestoreTL001_NoReport()
    {
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

    [Fact]
    public async Task PragmaDisableTL001_PreviousLineComment_NoReport()
    {
        var source = @"
class C
{
    void M()
    {
// Billing admin view
#pragma warning disable TL001
        var x = 1;
#pragma warning restore TL001
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task PragmaDisableMultipleTlRules_WithComment_NoReport()
    {
        var source = @"
class C
{
    void M()
    {
#pragma warning disable TL001, TL002 // Admin module
        var x = 1;
#pragma warning restore TL001, TL002
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }
}
