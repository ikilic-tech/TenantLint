using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL011Tests
{
    private const string EnableTL111 = @"
is_global = true
dotnet_diagnostic.TL111.severity = suggestion
";

    /// <summary>
    /// Creates a test instance that enables TL111 via editorconfig without suppressing it
    /// via DisabledDiagnostics (which would override the editorconfig setting).
    /// Only TL201 is suppressed to avoid interference from auto-inserted pragmas.
    /// </summary>
    private static CSharpAnalyzerTest<TenantIsolationAnalyzer, DefaultVerifier> CreateTL111Test(
        string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TenantIsolationAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>.Net80WithEfCore,
        };
        // Only suppress TL201, not TL111 — TL111 is being explicitly enabled via editorconfig
        test.DisabledDiagnostics.Add("TL201");
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", EnableTL111));
        test.ExpectedDiagnostics.AddRange(expected);
        return test;
    }

    // ===========================================================
    // TL111 — Info on DbContext (opt-in, constructor-only field)
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

        var test = CreateTL111Test(source, expected);
        await test.RunAsync(CancellationToken.None);
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

        var test = CreateTL111Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NonReadonlyCtorOnlyField_ReportsTL111()
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
    private string _tenantId;
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
        // Non-readonly but only assigned in ctor — TL111 fires (with opt-in)
        var expected = Verify.Diagnostic(Descriptors.TL111)
            .WithSpan(12, 20, 12, 29)
            .WithArguments("AppDbContext", "_tenantId");

        var test = CreateTL111Test(source, expected);
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task MutableFieldWithSetter_NoTL111()
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
        // Field assigned outside ctor — no TL111 even when enabled
        await Verify.VerifyWithEfCoreAndOptionsAsync(source, EnableTL111);
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
        await Verify.VerifyWithEfCoreAndOptionsAsync(source, EnableTL111, expected);
    }

    [Fact]
    public async Task TL111_DisabledByDefault()
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
        // TL111 is disabled by default — no diagnostics expected
        await Verify.VerifyWithEfCoreAsync(source);
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
        // TL011 fires at registration. TL111 is disabled by default.
        var expected = Verify.Diagnostic(Descriptors.TL011)
            .WithLocation(0)
            .WithArguments("AppDbContext", "AddDbContextPool", "_tenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
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
        var expected = Verify.Diagnostic(Descriptors.TL011)
            .WithLocation(0)
            .WithArguments("AppDbContext", "AddPooledDbContextFactory", "_tenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task AddDbContextPool_NonReadonlyCtorOnlyField_ReportsTL011()
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
    private string _tenantId;
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
        // Non-readonly but only assigned in ctor — TL011 fires
        var expected = Verify.Diagnostic(Descriptors.TL011)
            .WithLocation(0)
            .WithArguments("AppDbContext", "AddDbContextPool", "_tenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
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
        // Non-pooled: no TL011. TL111 is disabled by default.
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task AddDbContextPool_FieldWithSetter_NoTL011()
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
        // Field assigned outside ctor (SetTenant method): no TL011
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
