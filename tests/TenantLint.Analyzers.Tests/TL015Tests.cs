using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL015Tests
{
    // Usings must come first, then namespace stubs, then test types
    private const string Preamble = @"
using System;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.EntityFrameworkCore;

namespace Finbuckle.MultiTenant
{
    public interface ITenantInfo
    {
        string Id { get; set; }
        string Identifier { get; set; }
    }
}

namespace Finbuckle.MultiTenant.EntityFrameworkCore
{
    public enum TenantMismatchMode
    {
        Throw = 0,
        Ignore = 1,
        Overwrite = 2
    }

    public enum TenantNotSetMode
    {
        Throw = 0,
        Overwrite = 1
    }

    public interface IMultiTenantDbContext
    {
        TenantMismatchMode TenantMismatchMode { get; set; }
        TenantNotSetMode TenantNotSetMode { get; set; }
    }
}
";

    // ===========================================================
    // Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task TenantMismatchMode_Ignore_InConstructor_Reports()
    {
        var source = Preamble + @"
class AppDbContext : IMultiTenantDbContext
{
    public TenantMismatchMode TenantMismatchMode { get; set; }
    public TenantNotSetMode TenantNotSetMode { get; set; }

    public AppDbContext()
    {
        {|#0:TenantMismatchMode = TenantMismatchMode.Ignore|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL015)
            .WithLocation(0);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TenantMismatchMode_Ignore_InMethod_Reports()
    {
        var source = Preamble + @"
class MyService
{
    private readonly IMultiTenantDbContext _context;

    void Configure()
    {
        {|#0:_context.TenantMismatchMode = TenantMismatchMode.Ignore|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL015)
            .WithLocation(0);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TenantMismatchMode_Ignore_LocalVariable_Reports()
    {
        var source = Preamble + @"
class AppDbContext : IMultiTenantDbContext
{
    public TenantMismatchMode TenantMismatchMode { get; set; }
    public TenantNotSetMode TenantNotSetMode { get; set; }

    void Configure()
    {
        {|#0:TenantMismatchMode = TenantMismatchMode.Ignore|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL015)
            .WithLocation(0);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    // ===========================================================
    // Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task TenantMismatchMode_Throw_NoReport()
    {
        var source = Preamble + @"
class AppDbContext : IMultiTenantDbContext
{
    public TenantMismatchMode TenantMismatchMode { get; set; }
    public TenantNotSetMode TenantNotSetMode { get; set; }

    public AppDbContext()
    {
        TenantMismatchMode = TenantMismatchMode.Throw;
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task TenantMismatchMode_Overwrite_NoReport()
    {
        var source = Preamble + @"
class AppDbContext : IMultiTenantDbContext
{
    public TenantMismatchMode TenantMismatchMode { get; set; }
    public TenantNotSetMode TenantNotSetMode { get; set; }

    public AppDbContext()
    {
        TenantMismatchMode = TenantMismatchMode.Overwrite;
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task TenantMismatchMode_Ignore_WithCrossTenant_NoReport()
    {
        var source = Preamble + @"
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason = """") { }
}

class AppDbContext : IMultiTenantDbContext
{
    public TenantMismatchMode TenantMismatchMode { get; set; }
    public TenantNotSetMode TenantNotSetMode { get; set; }

    [CrossTenant(""legacy migration"")]
    void Configure()
    {
        TenantMismatchMode = TenantMismatchMode.Ignore;
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFinbuckleTypes_NoReport()
    {
        var source = @"
class C
{
    void M()
    {
        var x = 1;
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task UserEnum_TenantMismatchMode_WrongNamespace_NoReport()
    {
        var source = @"
namespace MyApp
{
    enum TenantMismatchMode
    {
        Throw = 0,
        Ignore = 1,
        Overwrite = 2
    }

    class C
    {
        TenantMismatchMode Mode { get; set; }
        void M()
        {
            Mode = TenantMismatchMode.Ignore;
        }
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }
}
