using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL007Tests
{
    /// <summary>
    /// Creates a test instance that does NOT suppress TL007 in DisabledDiagnostics.
    /// TL007 is suppressed in the shared verifiers to avoid interfering with other
    /// tests that use static tenant-named fields in their fixtures.
    /// </summary>
    private static CSharpAnalyzerTest<TenantIsolationAnalyzer, DefaultVerifier> CreateTL007Test(
        string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TenantIsolationAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            TestBehaviors = TestBehaviors.SkipSuppressionCheck,
        };
        // Only suppress TL111 (opt-in), not TL007
        test.DisabledDiagnostics.Add("TL111");
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    // ===========================================================
    // Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task StaticStringField_CurrentTenantId_Reports()
    {
        var source = @"
class TenantState
{
    public static string {|#0:CurrentTenantId|};
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("CurrentTenantId");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticField_ITenantContext_Reports()
    {
        var source = @"
interface ITenantContext
{
    string TenantId { get; }
}

class State
{
    public static ITenantContext {|#0:_ctx|};
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("_ctx");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticProperty_TenantId_Reports()
    {
        var source = @"
class Config
{
    public static string {|#0:TenantId|} { get; set; }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("TenantId");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    // ===========================================================
    // Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task NonStaticField_TenantId_NoReport()
    {
        var source = @"
class Service
{
    private string _tenantId;
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StaticField_AsyncLocal_NoReport()
    {
        var source = @"
using System.Threading;

class State
{
    public static readonly AsyncLocal<string> _tenantId = new AsyncLocal<string>();
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StaticField_DefaultSchema_NoReport()
    {
        var source = @"
class Config
{
    public static readonly string DefaultSchema = ""dbo"";
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StaticField_Counter_NoReport()
    {
        var source = @"
class Metrics
{
    public static int RequestCount;
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ConstField_TenantHeader_NoReport()
    {
        var source = @"
class Constants
{
    public const string TenantHeader = ""X-Tenant-Id"";
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }
}
