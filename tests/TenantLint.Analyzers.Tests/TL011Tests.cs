using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL011Tests
{
    // ===========================================================
    // TL111 — Info on DbContext (readonly tenant filter field)
    // ===========================================================

    [Fact]
    public async Task ReadonlyField_InFilter_ReportsTL111()
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
    private readonly string _tenantId;
    public DbSet<Order> Orders { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options, string tenantId) : base(options)
    {
        _tenantId = tenantId;
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == _tenantId);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL111)
            .WithSpan(12, 29, 12, 38)
            .WithArguments("AppDbContext", "_tenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task ReadonlyServiceField_InFilter_ReportsTL111()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public interface ITenantService { string TenantId { get; } }

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private readonly ITenantService _tenantService;
    public DbSet<Order> Orders { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantService svc) : base(options)
    {
        _tenantService = svc;
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == _tenantService.TenantId);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL111)
            .WithSpan(14, 37, 14, 51)
            .WithArguments("AppDbContext", "_tenantService");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task MutableField_InFilter_NoTL111()
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
    private string _tenantId = null!;
    public DbSet<Order> Orders { get; set; }

    public void SetTenant(string tenantId) { _tenantId = tenantId; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == _tenantId);
    }
}
";
        // Mutable field — no TL111
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task NoFilter_NoTL111()
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
    private readonly string _tenantId;
    public DbSet<Order> Orders { get; set; }

    public AppDbContext(string tenantId) { _tenantId = tenantId; }
}
";
        // TL005 fires (no filter), but NOT TL111 (no filter lambda to analyze)
        var expected = Verify.Diagnostic(Descriptors.TL005)
            .WithSpan(13, 25, 13, 31)
            .WithArguments("Order", "TenantId", "AppDbContext");
        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // TL011 — Warning at pool registration site
    // ===========================================================

    [Fact]
    public async Task AddDbContextPool_ReadonlyField_ReportsTL011()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private readonly string _tenantId;
    public DbSet<Order> Orders { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options, string tenantId) : base(options)
    {
        _tenantId = tenantId;
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == _tenantId);
    }
}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.{|#0:AddDbContextPool<AppDbContext>|}(o => { });
    }
}
";
        var expectedTL011 = Verify.Diagnostic(Descriptors.TL011)
            .WithLocation(0)
            .WithArguments("AppDbContext", "AddDbContextPool", "_tenantId");
        var expectedTL111 = Verify.Diagnostic(Descriptors.TL111)
            .WithSpan(13, 29, 13, 38)
            .WithArguments("AppDbContext", "_tenantId");

        await Verify.VerifyWithEfCoreAsync(source, expectedTL011, expectedTL111);
    }

    [Fact]
    public async Task AddPooledDbContextFactory_ReadonlyField_ReportsTL011()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private readonly string _tenantId;
    public DbSet<Order> Orders { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options, string tenantId) : base(options)
    {
        _tenantId = tenantId;
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == _tenantId);
    }
}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.{|#0:AddPooledDbContextFactory<AppDbContext>|}(o => { });
    }
}
";
        var expectedTL011 = Verify.Diagnostic(Descriptors.TL011)
            .WithLocation(0)
            .WithArguments("AppDbContext", "AddPooledDbContextFactory", "_tenantId");
        var expectedTL111 = Verify.Diagnostic(Descriptors.TL111)
            .WithSpan(13, 29, 13, 38)
            .WithArguments("AppDbContext", "_tenantId");

        await Verify.VerifyWithEfCoreAsync(source, expectedTL011, expectedTL111);
    }

    [Fact]
    public async Task AddDbContext_ReadonlyField_NoTL011()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private readonly string _tenantId;
    public DbSet<Order> Orders { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options, string tenantId) : base(options)
    {
        _tenantId = tenantId;
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == _tenantId);
    }
}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>(o => { });
    }
}
";
        // Non-pooled: TL111 fires (readonly field) but NOT TL011
        var expectedTL111 = Verify.Diagnostic(Descriptors.TL111)
            .WithSpan(13, 29, 13, 38)
            .WithArguments("AppDbContext", "_tenantId");

        await Verify.VerifyWithEfCoreAsync(source, expectedTL111);
    }

    [Fact]
    public async Task AddDbContextPool_MutableField_NoTL011()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tenantId = null!;
    public DbSet<Order> Orders { get; set; }

    public void SetTenant(string tenantId) { _tenantId = tenantId; }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Order>().HasQueryFilter(e => e.TenantId == _tenantId);
    }
}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContextPool<AppDbContext>(o => { });
    }
}
";
        // Mutable field: no TL011, no TL111
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task AddDbContextPool_NoFilter_NoTL011()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private readonly string _tenantId;
    public DbSet<Order> Orders { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options, string tenantId) : base(options)
    {
        _tenantId = tenantId;
    }
}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContextPool<AppDbContext>(o => { });
    }
}
";
        // No HasQueryFilter: TL005 fires, but NOT TL011 or TL111
        var expectedTL005 = Verify.Diagnostic(Descriptors.TL005)
            .WithSpan(14, 25, 14, 31)
            .WithArguments("Order", "TenantId", "AppDbContext");
        await Verify.VerifyWithEfCoreAsync(source, expectedTL005);
    }
}
