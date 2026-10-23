using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL202Tests
{
    // ===========================================================
    // Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task SuppressMessage_NoJustification_Reports()
    {
        var source = @"
using System.Diagnostics.CodeAnalysis;

class C
{
    [{|#0:SuppressMessage(""TenantIsolation"", ""TL001"")|}]
    void M() { }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL202)
            .WithLocation(0)
            .WithArguments("TL001");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task SuppressMessage_EmptyJustification_Reports()
    {
        var source = @"
using System.Diagnostics.CodeAnalysis;

class C
{
    [{|#0:SuppressMessage(""TenantIsolation"", ""TL001"", Justification = """")|}]
    void M() { }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL202)
            .WithLocation(0)
            .WithArguments("TL001");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task SuppressMessage_TodoJustification_Reports()
    {
        var source = @"
using System.Diagnostics.CodeAnalysis;

class C
{
    [{|#0:SuppressMessage(""TenantIsolation"", ""TL001"", Justification = ""TODO"")|}]
    void M() { }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL202)
            .WithLocation(0)
            .WithArguments("TL001");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    // ===========================================================
    // Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task SuppressMessage_WithJustification_NoReport()
    {
        var source = @"
using System.Diagnostics.CodeAnalysis;

class C
{
    [SuppressMessage(""TenantIsolation"", ""TL001"", Justification = ""Admin billing cross-tenant view"")]
    void M() { }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task SuppressMessage_NonTlRule_NoReport()
    {
        var source = @"
using System.Diagnostics.CodeAnalysis;

class C
{
    [SuppressMessage(""Style"", ""IDE0001"")]
    void M() { }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task SuppressMessage_WithMigrationJustification_NoReport()
    {
        var source = @"
using System.Diagnostics.CodeAnalysis;

class C
{
    [SuppressMessage(""TenantIsolation"", ""TL001"", Justification = ""Required for migration script"")]
    void M() { }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoSuppressMessage_NoReport()
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
    public async Task SuppressMessage_OnType_WithJustification_NoReport()
    {
        var source = @"
using System.Diagnostics.CodeAnalysis;

[SuppressMessage(""TenantIsolation"", ""TL001"", Justification = ""Admin module"")]
class C
{
    void M() { }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }
}
