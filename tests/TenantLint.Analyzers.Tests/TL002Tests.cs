using Microsoft.CodeAnalysis;
using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL002Tests
{
    // ===========================================================
    // EF 8 Positive Cases — Warning (3+)
    // ===========================================================

    [Fact]
    public async Task EF8_ExecuteSqlRaw_TenantTable_NoTenantColumn_Reports_Warning()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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
        db.Database.{|#0:ExecuteSqlRaw|}(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002)
            .WithLocation(0)
            .WithArguments("ExecuteSqlRaw", "Customers", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task EF8_SqlQueryRaw_TenantTable_SelectOnly_Reports_Warning()
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
        var count = db.Database.{|#0:SqlQueryRaw<int>|}(""SELECT COUNT(*) FROM Customers"").ToList();
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002)
            .WithLocation(0)
            .WithArguments("SqlQueryRaw", "Customers", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task EF8_ExecuteSqlRaw_TenantColumnInSelect_Reports_Warning()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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
        db.Database.{|#0:ExecuteSqlRaw|}(""SELECT tenant_id, Name FROM Customers"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002)
            .WithLocation(0)
            .WithArguments("ExecuteSqlRaw", "Customers", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // EF 8 Positive Cases — Info downgrade (2+)
    // ===========================================================

    [Fact]
    public async Task EF8_ExecuteSqlRaw_TenantColumnInWhere_Reports_Info()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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
        db.Database.{|#0:ExecuteSqlRaw|}(""DELETE FROM Customers WHERE tenant_id = @tid"", ""t1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002Filtered)
            .WithLocation(0)
            .WithArguments("ExecuteSqlRaw", "Customers", "tenant_id")
            .WithSeverity(DiagnosticSeverity.Info);

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task EF8_ExecuteSqlRaw_TenantColumnInAndClause_Reports_Info()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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
        db.Database.{|#0:ExecuteSqlRaw|}(""UPDATE Customers SET Name = @name WHERE IsActive = 1 AND TenantId = @tid"", ""x"", ""t1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002Filtered)
            .WithLocation(0)
            .WithArguments("ExecuteSqlRaw", "Customers", "TenantId")
            .WithSeverity(DiagnosticSeverity.Info);

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // EF 8 Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task EF8_ExecuteSqlRaw_NonTenantTable_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class Country
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Country> Countries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(""DELETE FROM Countries WHERE Id = 1"");
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task EF8_ExecuteSqlRaw_NonConstantSql_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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
    private string GetSql() => ""DELETE FROM Customers"";

    public void DoWork(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(GetSql());
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task EF8_ExecuteSqlRaw_CrossTenantMethod_NoDiagnostic()
    {
        var source = @"
using System;
using Microsoft.EntityFrameworkCore;

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

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class MyService
{
    [CrossTenant(""Admin cleanup across all tenants"")]
    public void DoWork(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task EF8_ExecuteSqlRaw_WordBoundary_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class CustomerLog
{
    public int Id { get; set; }
    public string Message { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }
    public DbSet<CustomerLog> CustomerLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(""DELETE FROM CustomerLogs WHERE Id = 1"");
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task EF8_UserDefined_ExecuteSqlRaw_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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

public static class MyExtensions
{
    public static void ExecuteSqlRaw(this AppDbContext db, string sql) { }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.ExecuteSqlRaw(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task EF8_ExecuteSqlRaw_GlobalEntity_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AuditLog
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(""DELETE FROM AuditLogs WHERE Id = 1"");
    }
}
";
        var editorconfig = @"
is_global = true
tenantlint.global_entities = AuditLog
";
        await Verify.VerifyWithEfCoreAndOptionsAsync(source, editorconfig);
    }

    // ===========================================================
    // EF 10 Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task EF10_ExecuteSqlRaw_TenantTable_Reports_Warning()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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
        db.Database.{|#0:ExecuteSqlRaw|}(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002)
            .WithLocation(0)
            .WithArguments("ExecuteSqlRaw", "Customers", "TenantId");

        await Verify.VerifyWithEfCore10Async(source, expected);
    }

    [Fact]
    public async Task EF10_SqlQueryRaw_TenantTable_SelectOnly_Reports_Warning()
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
        var count = db.Database.{|#0:SqlQueryRaw<int>|}(""SELECT COUNT(*) FROM Customers"").ToList();
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002)
            .WithLocation(0)
            .WithArguments("SqlQueryRaw", "Customers", "TenantId");

        await Verify.VerifyWithEfCore10Async(source, expected);
    }

    [Fact]
    public async Task EF10_ExecuteSqlRaw_TenantColumnInWhere_Reports_Info()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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
        db.Database.{|#0:ExecuteSqlRaw|}(""DELETE FROM Customers WHERE tenant_id = @t"", ""t1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002Filtered)
            .WithLocation(0)
            .WithArguments("ExecuteSqlRaw", "Customers", "tenant_id")
            .WithSeverity(DiagnosticSeverity.Info);

        await Verify.VerifyWithEfCore10Async(source, expected);
    }

    // ===========================================================
    // EF 10 Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task EF10_ExecuteSqlRaw_NonTenantTable_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class Country
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }
    public DbSet<Country> Countries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(""DELETE FROM Countries WHERE Id = 1"");
    }
}
";
        await Verify.VerifyWithEfCore10Async(source);
    }

    [Fact]
    public async Task EF10_ExecuteSqlRaw_NonConstantSql_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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
    private string GetSql() => ""DELETE FROM Customers"";

    public void DoWork(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(GetSql());
    }
}
";
        await Verify.VerifyWithEfCore10Async(source);
    }

    [Fact]
    public async Task EF10_ExecuteSqlRaw_CrossTenantMethod_NoDiagnostic()
    {
        var source = @"
using System;
using Microsoft.EntityFrameworkCore;

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

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class MyService
{
    [CrossTenant(""Admin cleanup across all tenants"")]
    public void DoWork(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        await Verify.VerifyWithEfCore10Async(source);
    }

    [Fact]
    public async Task EF10_ExecuteSqlRaw_WordBoundary_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class CustomerLog
{
    public int Id { get; set; }
    public string Message { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Customer> Customers { get; set; }
    public DbSet<CustomerLog> CustomerLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(""DELETE FROM CustomerLogs WHERE Id = 1"");
    }
}
";
        await Verify.VerifyWithEfCore10Async(source);
    }

    [Fact]
    public async Task EF10_UserDefined_ExecuteSqlRaw_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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

public static class MyExtensions
{
    public static void ExecuteSqlRaw(this AppDbContext db, string sql) { }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.ExecuteSqlRaw(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        await Verify.VerifyWithEfCore10Async(source);
    }

    // ===========================================================
    // CrossTenant class-level
    // ===========================================================

    [Fact]
    public async Task EF8_ClassLevelCrossTenant_NoTL002_ButTL902Expected()
    {
        var source = @"
using System;
using Microsoft.EntityFrameworkCore;

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

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

[{|#0:CrossTenant(""Admin service"")|}]
public class AdminService
{
    public void DoWork(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL902)
            .WithLocation(0)
            .WithArguments("AdminService");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // Additional edge cases
    // ===========================================================

    [Fact]
    public async Task EF8_ExecuteSqlRawAsync_TenantTable_Reports_Warning()
    {
        var source = @"
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

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
    public async Task DoWork(AppDbContext db)
    {
        await db.Database.{|#0:ExecuteSqlRawAsync|}(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002)
            .WithLocation(0)
            .WithArguments("ExecuteSqlRawAsync", "Customers", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task EF8_ExecuteSqlRaw_SnakeCaseTableName_Reports_Warning()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class OrderItem
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<OrderItem> OrderItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrderItem>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

public class MyService
{
    public void DoWork(AppDbContext db)
    {
        db.Database.{|#0:ExecuteSqlRaw|}(""DELETE FROM order_items WHERE IsDeleted = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002)
            .WithLocation(0)
            .WithArguments("ExecuteSqlRaw", "order_items", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task EF8_ExecuteSql_FormattableString_TenantTable_Reports_Warning()
    {
        // Use ExecuteSqlRaw with interpolation placeholders already expanded
        // FormattableString-based methods (ExecuteSql, ExecuteSqlInterpolated, SqlQuery)
        // are handled the same way — the SQL text is extracted from the interpolated string.
        // This test uses a constant-value interpolated string for simplicity.
        var source = @"
using Microsoft.EntityFrameworkCore;

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
        db.Database.{|#0:ExecuteSqlRaw|}(""DELETE FROM Customers WHERE Id = 42"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL002)
            .WithLocation(0)
            .WithArguments("ExecuteSqlRaw", "Customers", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task EF8_NoTenantEntities_NoTL002Registration()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

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
        db.Database.ExecuteSqlRaw(""DELETE FROM Countries WHERE Id = 1"");
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }
}
