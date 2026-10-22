using Microsoft.CodeAnalysis;
using TenantLint.CodeFixes;
using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;
using CodeFixVerify = CSharpCodeFixVerifier<TenantIsolationAnalyzer, TenantIsolationCodeFixProvider>;

public class TL001Tests
{
    // ===========================================================
    // EF 8 Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task EF8_ParameterlessIgnoreQueryFilters_OnTenantEntity_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.{|#0:IgnoreQueryFilters|}().ToList();
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL001NoSelective)
            .WithLocation(0)
            .WithArguments("Customer");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task EF8_IgnoreQueryFilters_InExecuteUpdateChain_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public string Name { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.Customers.{|#0:IgnoreQueryFilters|}().ExecuteUpdate(s => s.SetProperty(c => c.Name, ""x""));
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL001NoSelective)
            .WithLocation(0)
            .WithArguments("Customer");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task EF8_IgnoreQueryFilters_InExecuteDeleteChain_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.Customers.{|#0:IgnoreQueryFilters|}().ExecuteDelete();
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL001NoSelective)
            .WithLocation(0)
            .WithArguments("Customer");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // EF 8 Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task EF8_IgnoreQueryFilters_OnNonTenantEntity_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Country
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Country> Countries { get; set; }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Countries.IgnoreQueryFilters().ToList();
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task EF8_CrossTenantMethod_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    [CrossTenant(""Admin migration"")]
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.IgnoreQueryFilters().ToList();
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task EF8_CrossTenantClass_NoDiagnostic_ButTL902()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

[{|#0:CrossTenant(""Admin service"")|}]
public class MyService
{
    public void DoWork(AppDbContext db)
    {
        // No TL001 because class has [CrossTenant]
        var q = db.Customers.IgnoreQueryFilters().ToList();
    }
}
";
        var expected = Verify.Diagnostic("TL902")
            .WithLocation(0)
            .WithArguments("MyService");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task EF8_UserDefinedIgnoreQueryFilters_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public static class QueryExtensions
{
    public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.AsQueryable().IgnoreQueryFilters().ToList();
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task EF8_GlobalEntity_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class AuditLog
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<AuditLog> AuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.AuditLogs.IgnoreQueryFilters().ToList();
    }
}
";
        var editorconfig = @"
is_global = true
tenantlint.global_entities = AuditLog
";
        await Verify.VerifyWithEfCoreAndOptionsAsync(source, editorconfig);
    }

    [Fact]
    public async Task EF8_ConventionOnlyEntity_Reports()
    {
        // Entity has TenantId but no HasQueryFilter — it's still tenant-scoped by convention
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.{|#0:IgnoreQueryFilters|}().ToList();
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL001NoSelective)
            .WithLocation(0)
            .WithArguments("Customer");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // EF 10 Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task EF10_ParameterlessIgnoreQueryFilters_WithNonTenantNamedFilter_Reports_WithSelectiveSuggestion()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsDeleted { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
        modelBuilder.Entity<Customer>().HasQueryFilter(""SoftDelete"", e => !e.IsDeleted);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.{|#0:IgnoreQueryFilters|}().ToList();
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL001)
            .WithLocation(0)
            .WithArguments("Customer", @"""SoftDelete""");

        await Verify.VerifyWithEfCore10Async(source, expected);
    }

    [Fact]
    public async Task EF10_ParameterlessIgnoreQueryFilters_WithOnlyTenantNamedFilter_Reports_NoSelectiveSuggestion()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.{|#0:IgnoreQueryFilters|}().ToList();
    }
}
";
        // No selective suggestion because the only filter IS the tenant filter
        var expected = Verify.Diagnostic(Descriptors.TL001NoSelective)
            .WithLocation(0)
            .WithArguments("Customer");

        await Verify.VerifyWithEfCore10Async(source, expected);
    }

    [Fact]
    public async Task EF10_IgnoreQueryFilters_InExecuteDeleteChain_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsDeleted { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
        modelBuilder.Entity<Customer>().HasQueryFilter(""SoftDelete"", e => !e.IsDeleted);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.Customers.{|#0:IgnoreQueryFilters|}().ExecuteDelete();
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL001)
            .WithLocation(0)
            .WithArguments("Customer", @"""SoftDelete""");

        await Verify.VerifyWithEfCore10Async(source, expected);
    }

    // ===========================================================
    // EF 10 Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task EF10_SelectiveIgnoreQueryFilters_PreservingTenantFilter_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsDeleted { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
        modelBuilder.Entity<Customer>().HasQueryFilter(""SoftDelete"", e => !e.IsDeleted);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        // Selective: only removes SoftDelete, tenant filter remains
        var q = db.Customers.IgnoreQueryFilters([""SoftDelete""]).ToList();
    }
}
";
        await Verify.VerifyWithEfCore10Async(source);
    }

    [Fact]
    public async Task EF10_CrossTenantMethod_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsDeleted { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
        modelBuilder.Entity<Customer>().HasQueryFilter(""SoftDelete"", e => !e.IsDeleted);
    }
}

public class MyService
{
    [CrossTenant(""Admin bulk op"")]
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.IgnoreQueryFilters().ToList();
    }
}
";
        await Verify.VerifyWithEfCore10Async(source);
    }

    [Fact]
    public async Task EF10_NonTenantEntity_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Country
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Country> Countries { get; set; }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Countries.IgnoreQueryFilters().ToList();
    }
}
";
        await Verify.VerifyWithEfCore10Async(source);
    }

    [Fact]
    public async Task EF10_UserDefinedIgnoreQueryFilters_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
    }
}

public static class QueryExtensions
{
    public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.AsQueryable().IgnoreQueryFilters().ToList();
    }
}
";
        await Verify.VerifyWithEfCore10Async(source);
    }

    [Fact]
    public async Task EF10_GlobalEntity_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class AuditLog
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<AuditLog> AuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.AuditLogs.IgnoreQueryFilters().ToList();
    }
}
";
        var editorconfig = @"
is_global = true
tenantlint.global_entities = AuditLog
";
        await Verify.VerifyWithEfCore10AndOptionsAsync(source, editorconfig);
    }

    [Fact]
    public async Task EF10_MultipleNonTenantFilters_AllIncludedInSuggestion()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsArchived { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
        modelBuilder.Entity<Customer>().HasQueryFilter(""SoftDelete"", e => !e.IsDeleted);
        modelBuilder.Entity<Customer>().HasQueryFilter(""Archive"", e => !e.IsArchived);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.{|#0:IgnoreQueryFilters|}().ToList();
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL001)
            .WithLocation(0)
            .WithArguments("Customer", @"""SoftDelete"", ""Archive""");

        await Verify.VerifyWithEfCore10Async(source, expected);
    }

    // ===========================================================
    // Code Fix Tests
    // ===========================================================

    [Fact]
    public async Task CodeFix_EF10_SelectiveIgnoreQueryFilters_Rewrites()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsDeleted { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
        modelBuilder.Entity<Customer>().HasQueryFilter(""SoftDelete"", e => !e.IsDeleted);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.{|#0:IgnoreQueryFilters|}().ToList();
    }
}
";

        var fixedSource = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsDeleted { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
        modelBuilder.Entity<Customer>().HasQueryFilter(""SoftDelete"", e => !e.IsDeleted);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.IgnoreQueryFilters([""SoftDelete""]).ToList();
    }
}
";

        var expected = new[]
        {
            CodeFixVerify.Diagnostic(Descriptors.TL001)
                .WithLocation(0)
                .WithArguments("Customer", @"""SoftDelete""")
        };

        await CodeFixVerify.VerifyCodeFixAsync(
            source, expected, fixedSource,
            referenceAssemblies: Verify.Net100WithEfCore10,
            equivalenceKey: "TL001_Selective");
    }

    [Fact]
    public async Task CodeFix_AddCrossTenantAttribute()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.{|#0:IgnoreQueryFilters|}().ToList();
    }
}
";

        var fixedSource = @"
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    [CrossTenant("""")]
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.IgnoreQueryFilters().ToList();
    }
}
";

        var expected = new[]
        {
            CodeFixVerify.Diagnostic(Descriptors.TL001NoSelective)
                .WithLocation(0)
                .WithArguments("Customer")
        };

        // After fix, the empty reason triggers TL901
        var remaining = new[]
        {
            CodeFixVerify.Diagnostic("TL901")
                .WithSpan(31, 6, 31, 21)
                .WithArguments("DoWork")
        };

        await CodeFixVerify.VerifyCodeFixAsync(
            source, expected, fixedSource,
            equivalenceKey: "TL001_CrossTenant",
            remainingDiagnostics: remaining);
    }

    [Fact]
    public async Task CodeFix_EF8_NoCrossTenantAttribute_NoFixOffered()
    {
        // When CrossTenantAttribute doesn't exist in compilation AND no selective overload (EF8),
        // no code fix should be offered.
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.{|#0:IgnoreQueryFilters|}().ToList();
    }
}
";

        var expected = new[]
        {
            CodeFixVerify.Diagnostic(Descriptors.TL001NoSelective)
                .WithLocation(0)
                .WithArguments("Customer")
        };

        // FixedSource == source means no fix is applied
        await CodeFixVerify.VerifyCodeFixAsync(source, expected, source);
    }

    [Fact]
    public async Task CodeFix_EF10_NoCrossTenantAttribute_OnlySelectiveFixOffered()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsDeleted { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
        modelBuilder.Entity<Customer>().HasQueryFilter(""SoftDelete"", e => !e.IsDeleted);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.{|#0:IgnoreQueryFilters|}().ToList();
    }
}
";

        var fixedSource = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsDeleted { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
        modelBuilder.Entity<Customer>().HasQueryFilter(""SoftDelete"", e => !e.IsDeleted);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        var q = db.Customers.IgnoreQueryFilters([""SoftDelete""]).ToList();
    }
}
";

        var expected = new[]
        {
            CodeFixVerify.Diagnostic(Descriptors.TL001)
                .WithLocation(0)
                .WithArguments("Customer", @"""SoftDelete""")
        };

        // No CrossTenantAttribute in compilation, so only selective fix should be offered
        await CodeFixVerify.VerifyCodeFixAsync(
            source, expected, fixedSource,
            referenceAssemblies: Verify.Net100WithEfCore10,
            equivalenceKey: "TL001_Selective");
    }
}
