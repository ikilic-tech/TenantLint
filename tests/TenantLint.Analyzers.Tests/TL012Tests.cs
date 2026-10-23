using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL012Tests
{
    // ===========================================================
    // Positive — Warning (TL012)
    // ===========================================================

    [Fact]
    public async Task LocalVariable_InFilterLambda_ReportsTL012()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private readonly string _tenantId = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        var localTid = _tenantId;
        m.Entity<Order>().{|#0:HasQueryFilter|}(e => e.TenantId == localTid);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL012)
            .WithLocation(0)
            .WithArguments("Order", "AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task StaticField_InFilterLambda_ReportsTL012()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public static string CurrentTenantId = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().{|#0:HasQueryFilter|}(e => e.TenantId == CurrentTenantId);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL012)
            .WithLocation(0)
            .WithArguments("Order", "AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task StaticProperty_OnExternalClass_ReportsTL012()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public static class TenantHelper
{
    public static string Current { get; set; } = ""t1"";
}

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().{|#0:HasQueryFilter|}(e => e.TenantId == TenantHelper.Current);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL012)
            .WithLocation(0)
            .WithArguments("Order", "AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task StaticMethodCall_InFilterLambda_ReportsTL012()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public static class TenantHelper
{
    public static string GetCurrent() => ""t1"";
}

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().{|#0:HasQueryFilter|}(e => e.TenantId == TenantHelper.GetCurrent());
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL012)
            .WithLocation(0)
            .WithArguments("Order", "AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // Negative — no diagnostic (TL012)
    // ===========================================================

    [Fact]
    public async Task InstanceField_InFilterLambda_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private readonly string _tenantId = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == _tenantId);
    }
}
";
        // No TL012, but TL111 fires (readonly field)
        var expected = Verify.Diagnostic(Descriptors.TL111)
            .WithSpan(12, 29, 12, 38)
            .WithArguments("AppDbContext", "_tenantId");
        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task PropertyOnInstanceField_InFilterLambda_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public interface ITenantService { string TenantId { get; } }
public class TenantService : ITenantService { public string TenantId => ""t1""; }

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private readonly ITenantService _tenantService;
    public DbSet<Order> Orders { get; set; }

    public AppDbContext(ITenantService tenantService) { _tenantService = tenantService; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == _tenantService.TenantId);
    }
}
";
        // No TL012, but TL111 fires (readonly field)
        var expected = Verify.Diagnostic(Descriptors.TL111)
            .WithSpan(15, 37, 15, 51)
            .WithArguments("AppDbContext", "_tenantService");
        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task MethodCallOnInstanceField_InFilterLambda_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class TenantResolver
{
    public string GetTenantId() => ""t1"";
}

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private readonly TenantResolver _resolver;
    public DbSet<Order> Orders { get; set; }

    public AppDbContext(TenantResolver resolver) { _resolver = resolver; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == _resolver.GetTenantId());
    }
}
";
        // No TL012, but TL111 fires (readonly field)
        var expected = Verify.Diagnostic(Descriptors.TL111)
            .WithSpan(17, 37, 17, 46)
            .WithArguments("AppDbContext", "_resolver");
        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task ExplicitThis_InFilterLambda_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tenantId = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == this._tenantId);
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task NonTenantFilter_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsDeleted { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => !e.IsDeleted);
    }
}
";
        // TL005 fires (no tenant filter), but TL012 does not (no tenant property in lambda)
        var expected = Verify.Diagnostic(Descriptors.TL005)
            .WithSpan(13, 25, 13, 31)
            .WithArguments("Order", "TenantId", "AppDbContext");
        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task NoFilter_NoDiagnostic_TL012()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }
}
";
        // TL005 fires, but TL012 does not
        var expected = Verify.Diagnostic(Descriptors.TL005)
            .WithSpan(12, 25, 12, 31)
            .WithArguments("Order", "TenantId", "AppDbContext");
        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task ConstantLiteral_InFilterLambda_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId != """");
    }
}
";
        // Empty string is a constant — no TL012
        await Verify.VerifyWithEfCoreAsync(source);
    }
}
