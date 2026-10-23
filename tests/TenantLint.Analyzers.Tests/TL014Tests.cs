using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL014Tests
{
    // Usings must come first, then namespace stubs, then test types
    private const string Preamble = @"
using System;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;

namespace Volo.Abp.Data
{
    public interface IDataFilter
    {
        System.IDisposable Disable<TFilter>() where TFilter : class;
        System.IDisposable Enable<TFilter>() where TFilter : class;
    }

    public interface IDataFilter<TFilter> where TFilter : class
    {
        System.IDisposable Disable();
        System.IDisposable Enable();
    }
}

namespace Volo.Abp.MultiTenancy
{
    public interface IMultiTenant
    {
        System.Guid? TenantId { get; }
    }

    public interface ICurrentTenant
    {
        System.Guid? Id { get; }
        string Name { get; }
        System.IDisposable Change(System.Guid? id, string name = null);
    }
}
";

    // ===========================================================
    // Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task DataFilter_DisableIMultiTenant_Reports()
    {
        var source = Preamble + @"
class MyService
{
    private readonly IDataFilter _dataFilter;

    void DoWork()
    {
        using var _ = _dataFilter.{|#0:Disable<IMultiTenant>|}();
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL014)
            .WithLocation(0)
            .WithArguments("IDataFilter.Disable<IMultiTenant>");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task TypedDataFilter_Disable_Reports()
    {
        var source = Preamble + @"
class MyService
{
    private readonly IDataFilter<IMultiTenant> _filter;

    void DoWork()
    {
        using var _ = _filter.{|#0:Disable|}();
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL014)
            .WithLocation(0)
            .WithArguments("IDataFilter<IMultiTenant>.Disable");

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task CurrentTenant_Change_Reports()
    {
        var source = Preamble + @"
class MyService
{
    private readonly ICurrentTenant _currentTenant;

    void DoWork()
    {
        using var _ = _currentTenant.{|#0:Change|}(null);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL014Change)
            .WithLocation(0);

        await Verify.VerifyAnalyzerAsync(source, expected);
    }

    // ===========================================================
    // Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task DataFilter_DisableIMultiTenant_WithCrossTenant_NoReport()
    {
        var source = Preamble + @"
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason = """") { }
}

class MyService
{
    private readonly IDataFilter _dataFilter;

    [CrossTenant(""admin view"")]
    void DoWork()
    {
        using var _ = _dataFilter.Disable<IMultiTenant>();
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task DataFilter_DisableISoftDelete_NoReport()
    {
        var source = Preamble + @"
interface ISoftDelete { }

class MyService
{
    private readonly IDataFilter _dataFilter;

    void DoWork()
    {
        using var _ = _dataFilter.Disable<ISoftDelete>();
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task DataFilter_EnableIMultiTenant_NoReport()
    {
        var source = Preamble + @"
class MyService
{
    private readonly IDataFilter _dataFilter;

    void DoWork()
    {
        using var _ = _dataFilter.Enable<IMultiTenant>();
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoAbpTypes_NoReport()
    {
        var source = @"
class MyService
{
    void DoWork()
    {
        var x = 1;
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task UserMethodNamedDisable_NonAbpType_NoReport()
    {
        var source = @"
interface IMyFilter
{
    void Disable();
}

class MyService
{
    private readonly IMyFilter _filter;

    void DoWork()
    {
        _filter.Disable();
    }
}
";
        await Verify.VerifyAnalyzerAsync(source);
    }
}
