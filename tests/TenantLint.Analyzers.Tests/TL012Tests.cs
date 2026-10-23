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
        // No TL012 (instance field is rooted in this). TL111 is disabled by default.
        await Verify.VerifyWithEfCoreAsync(source);
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
        // No TL012 (property on instance field is rooted in this). TL111 is disabled by default.
        await Verify.VerifyWithEfCoreAsync(source);
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
        // No TL012 (method call on instance field is rooted in this). TL111 is disabled by default.
        await Verify.VerifyWithEfCoreAsync(source);
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

    // ===========================================================
    // Non-comparison forms (Contains, etc.)
    // ===========================================================

    [Fact]
    public async Task StaticListContains_InFilterLambda_ReportsTL012()
    {
        var source = @"
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public static class TenantHelper
{
    public static List<string> AllowedTenants { get; set; } = new();
}

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().{|#0:HasQueryFilter|}(e => TenantHelper.AllowedTenants.Contains(e.TenantId));
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL012)
            .WithLocation(0)
            .WithArguments("Order", "AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task AsyncLocalStaticProperty_InFilterLambda_ReportsTL012()
    {
        var source = @"
using System.Threading;
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public static class TenantContext
{
    private static readonly AsyncLocal<string> _current = new();
    public static string Current
    {
        get => _current.Value ?? """";
        set => _current.Value = value;
    }
}

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().{|#0:HasQueryFilter|}(e => e.TenantId == TenantContext.Current);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL012)
            .WithLocation(0)
            .WithArguments("Order", "AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task InstanceListContains_InFilterLambda_NoDiagnostic()
    {
        var source = @"
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private List<string> _allowedTenants = new();
    public DbSet<Order> Orders { get; set; }

    public void SetAllowedTenants(List<string> tenants) { _allowedTenants = tenants; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => _allowedTenants.Contains(e.TenantId));
    }
}
";
        // Instance field rooted in this — no TL012. Mutable — no TL111.
        await Verify.VerifyWithEfCoreAsync(source);
    }
}
