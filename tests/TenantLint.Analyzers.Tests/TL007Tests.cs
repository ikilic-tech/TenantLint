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
            .WithArguments("field", "CurrentTenantId");

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
            .WithArguments("field", "_ctx");

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
            .WithArguments("property", "TenantId");

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

    // ===========================================================
    // Static readonly narrowing: DTOs/value objects are safe
    // ===========================================================

    [Fact]
    public async Task StaticReadonly_TenantThemeDto_Default_NoReport()
    {
        var source = @"
class TenantThemeDto
{
    public string PrimaryColor { get; set; }
    public static readonly TenantThemeDto Default = new TenantThemeDto();
}
";
        var test = CreateTL007Test(source);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticReadonly_TenantSettings_Default_NoReport()
    {
        var source = @"
class TenantSettings
{
    public int MaxUsers { get; set; }
    public static readonly TenantSettings Default = new TenantSettings();
}
";
        var test = CreateTL007Test(source);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticReadonly_TenantOptions_NoReport()
    {
        var source = @"
class TenantOptions
{
    public string ConnectionString { get; set; }
    public static readonly TenantOptions Default = new TenantOptions();
}
";
        var test = CreateTL007Test(source);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticGetOnly_TenantConfig_NoReport()
    {
        var source = @"
class TenantConfig
{
    public static TenantConfig Default { get; } = new TenantConfig();
}
";
        var test = CreateTL007Test(source);
        await test.RunAsync(CancellationToken.None);
    }

    // ===========================================================
    // Static readonly: tenant-context types SHOULD still report
    // ===========================================================

    [Fact]
    public async Task StaticProperty_TenantContext_GetSet_Reports()
    {
        var source = @"
class TenantContext
{
    public string TenantId { get; set; }
}

class State
{
    public static TenantContext {|#0:Current|} { get; set; }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("property", "Current");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticReadonly_TenantContext_Instance_Reports()
    {
        var source = @"
class TenantContext
{
    public string TenantId { get; set; }
}

class State
{
    public static readonly TenantContext {|#0:Instance|} = new TenantContext();
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("field", "Instance");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticReadonly_CurrentTenant_Reports()
    {
        var source = @"
class CurrentTenant
{
    public string Id { get; set; }
}

class State
{
    public static readonly CurrentTenant {|#0:Instance|} = new CurrentTenant();
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("field", "Instance");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticReadonly_TenantProvider_Reports()
    {
        var source = @"
class TenantProvider
{
    public string GetTenantId() => """";
}

class State
{
    public static readonly TenantProvider {|#0:Provider|} = new TenantProvider();
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("field", "Provider");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticReadonly_TenantAccessor_Reports()
    {
        var source = @"
class TenantAccessor
{
    public string TenantId { get; set; }
}

class State
{
    public static readonly TenantAccessor {|#0:Accessor|} = new TenantAccessor();
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("field", "Accessor");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticReadonly_TenantInfo_Reports()
    {
        var source = @"
class TenantInfo
{
    public string TenantId { get; set; }
}

class State
{
    public static readonly TenantInfo {|#0:Info|} = new TenantInfo();
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("field", "Info");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    // ===========================================================
    // Message wording: "Static field" vs "Static property"
    // ===========================================================

    [Fact]
    public async Task StaticField_MessageSaysField()
    {
        var source = @"
class State
{
    public static string {|#0:TenantId|};
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithMessage("Static field 'TenantId' holds tenant state; static members are shared across all requests and tenants");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticAutoProperty_MessageSaysProperty()
    {
        var source = @"
class State
{
    public static string {|#0:TenantId|} { get; set; }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithMessage("Static property 'TenantId' holds tenant state; static members are shared across all requests and tenants");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StaticAutoProperty_ReportsOnceOnProperty()
    {
        // An auto-property has a compiler-generated backing field
        // (<CurrentTenantId>k__BackingField). The field is IsImplicitlyDeclared
        // and should be skipped — only the property itself reports.
        var source = @"
class TenantContext
{
    public static string {|#0:CurrentTenantId|} { get; set; }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("property", "CurrentTenantId");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    // ===========================================================
    // Mutable non-readonly fields: still report regardless of type
    // ===========================================================

    [Fact]
    public async Task StaticNonReadonly_TenantThemeDto_StillReports()
    {
        // Non-readonly static fields are always reported (mutable state)
        var source = @"
class TenantThemeDto
{
    public string PrimaryColor { get; set; }
}

class State
{
    public static TenantThemeDto {|#0:Current|};
}
";
        var expected = Verify.Diagnostic(Descriptors.TL007)
            .WithLocation(0)
            .WithArguments("field", "Current");

        var test = CreateTL007Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }
}
