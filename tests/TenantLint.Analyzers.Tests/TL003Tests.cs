using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL003Tests
{
    // ===========================================================
    // Positive Cases — Warning
    // ===========================================================

    [Fact]
    public async Task Dapper_Execute_TenantTable_NoTenantColumn_Reports_Warning()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.{|#0:Execute|}(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL003)
            .WithLocation(0)
            .WithArguments("Execute", "Customers", "TenantId");

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    [Fact]
    public async Task Dapper_Query_TenantTable_NoTenantColumn_Reports_Warning()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        var rows = conn.{|#0:Query<Customer>|}(""SELECT * FROM Customers"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL003)
            .WithLocation(0)
            .WithArguments("Query", "Customers", "TenantId");

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    [Fact]
    public async Task Dapper_Execute_TenantColumnInSelect_Reports_Warning()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.{|#0:Execute|}(""SELECT tenant_id, Name FROM Customers"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL003)
            .WithLocation(0)
            .WithArguments("Execute", "Customers", "TenantId");

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    // ===========================================================
    // Positive Cases — Info downgrade
    // ===========================================================

    [Fact]
    public async Task Dapper_Execute_TenantColumnInWhere_Reports_Info()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.{|#0:Execute|}(""DELETE FROM Customers WHERE tenant_id = @tid"", new { tid = ""t1"" });
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL103)
            .WithLocation(0)
            .WithArguments("Execute", "Customers", "tenant_id")
;

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    // ===========================================================
    // CommandDefinition
    // ===========================================================

    [Fact]
    public async Task Dapper_Execute_CommandDefinition_TenantTable_Reports_Warning()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        var cmd = new CommandDefinition(""DELETE FROM Customers WHERE IsDeleted = 1"");
        conn.{|#0:Execute|}(cmd);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL003)
            .WithLocation(0)
            .WithArguments("Execute", "Customers", "TenantId");

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    [Fact]
    public async Task Dapper_Query_CommandDefinition_TenantColumnInWhere_Reports_Info()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        var cmd = new CommandDefinition(""DELETE FROM Customers WHERE TenantId = @tid"");
        conn.{|#0:Execute|}(cmd);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL103)
            .WithLocation(0)
            .WithArguments("Execute", "Customers", "TenantId")
;

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    // ===========================================================
    // Async variant
    // ===========================================================

    [Fact]
    public async Task Dapper_ExecuteAsync_TenantTable_Reports_Warning()
    {
        var source = @"
using System.Data;
using System.Threading.Tasks;
using Dapper;
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
    public async Task DoWork(IDbConnection conn)
    {
        await conn.{|#0:ExecuteAsync|}(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL003)
            .WithLocation(0)
            .WithArguments("ExecuteAsync", "Customers", "TenantId");

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    // ===========================================================
    // Additional method variants
    // ===========================================================

    [Fact]
    public async Task Dapper_QueryFirstOrDefault_TenantTable_Reports_Warning()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        var c = conn.{|#0:QueryFirstOrDefault<Customer>|}(""SELECT * FROM Customers WHERE Id = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL003)
            .WithLocation(0)
            .WithArguments("QueryFirstOrDefault", "Customers", "TenantId");

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    [Fact]
    public async Task Dapper_ExecuteScalar_TenantTable_Reports_Warning()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        var count = conn.{|#0:ExecuteScalar<int>|}(""SELECT COUNT(*) FROM Customers"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL003)
            .WithLocation(0)
            .WithArguments("ExecuteScalar", "Customers", "TenantId");

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    [Fact]
    public async Task Dapper_ExecuteReader_TenantTable_Reports_Warning()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        var reader = conn.{|#0:ExecuteReader|}(""SELECT * FROM Customers"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL003)
            .WithLocation(0)
            .WithArguments("ExecuteReader", "Customers", "TenantId");

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    // ===========================================================
    // Negative Cases
    // ===========================================================

    [Fact]
    public async Task Dapper_Execute_NonTenantTable_NoDiagnostic()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.Execute(""DELETE FROM Countries WHERE Id = 1"");
    }
}
";
        await Verify.VerifyWithDapperAsync(source);
    }

    [Fact]
    public async Task Dapper_Execute_NonConstantSql_NoDiagnostic()
    {
        var source = @"
using System.Data;
using Dapper;
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

    public void DoWork(IDbConnection conn)
    {
        conn.Execute(GetSql());
    }
}
";
        await Verify.VerifyWithDapperAsync(source);
    }

    [Fact]
    public async Task Dapper_Execute_CrossTenantMethod_NoDiagnostic()
    {
        var source = @"
using System;
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.Execute(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        await Verify.VerifyWithDapperAsync(source);
    }

    [Fact]
    public async Task Dapper_Execute_WordBoundary_NoDiagnostic()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.Execute(""DELETE FROM CustomerLogs WHERE Id = 1"");
    }
}
";
        await Verify.VerifyWithDapperAsync(source);
    }

    [Fact]
    public async Task UserDefined_Query_NotDapper_NoDiagnostic()
    {
        var source = @"
using System.Data;
using Dapper;
using System.Collections.Generic;
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
    public static IEnumerable<T> Query<T>(this IDbConnection conn, string sql) => new List<T>();
}

public class MyService
{
    public void DoWork(IDbConnection conn)
    {
        var rows = MyExtensions.Query<Customer>(conn, ""SELECT * FROM Customers"");
    }
}
";
        await Verify.VerifyWithDapperAsync(source);
    }

    [Fact]
    public async Task Dapper_ClassLevelCrossTenant_NoTL003_ButTL902Expected()
    {
        var source = @"
using System;
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.Execute(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL902)
            .WithLocation(0)
            .WithArguments("AdminService");

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    [Fact]
    public async Task Dapper_Execute_GlobalEntity_NoDiagnostic()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.Execute(""DELETE FROM AuditLogs WHERE Id = 1"");
    }
}
";
        var editorconfig = @"
is_global = true
tenantlint.global_entities = AuditLog
";
        var test = new Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<TenantIsolationAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>.Net80WithEfCoreAndDapper,
        };
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", editorconfig));
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dapper_Execute_SnakeCaseTableName_Reports_Warning()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.{|#0:Execute|}(""DELETE FROM order_items WHERE IsDeleted = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL003)
            .WithLocation(0)
            .WithArguments("Execute", "order_items", "TenantId");

        await Verify.VerifyWithDapperAsync(source, expected);
    }

    [Fact]
    public async Task Dapper_Execute_NoTenantEntities_NoDiagnostic()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.Execute(""DELETE FROM Countries WHERE Id = 1"");
    }
}
";
        await Verify.VerifyWithDapperAsync(source);
    }

    // ===========================================================
    // EF Core 10 tests
    // ===========================================================

    [Fact]
    public async Task EF10_Dapper_Execute_TenantTable_Reports_Warning()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.{|#0:Execute|}(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL003)
            .WithLocation(0)
            .WithArguments("Execute", "Customers", "TenantId");

        await Verify.VerifyWithDapper10Async(source, expected);
    }

    [Fact]
    public async Task EF10_Dapper_Query_TenantColumnInWhere_Reports_Info()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        var rows = conn.{|#0:Query<Customer>|}(""SELECT * FROM Customers WHERE tenant_id = @tid"", new { tid = ""t1"" });
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL103)
            .WithLocation(0)
            .WithArguments("Query", "Customers", "tenant_id")
;

        await Verify.VerifyWithDapper10Async(source, expected);
    }

    [Fact]
    public async Task EF10_Dapper_Execute_NonTenantTable_NoDiagnostic()
    {
        var source = @"
using System.Data;
using Dapper;
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
    public void DoWork(IDbConnection conn)
    {
        conn.Execute(""DELETE FROM Countries WHERE Id = 1"");
    }
}
";
        await Verify.VerifyWithDapper10Async(source);
    }

    [Fact]
    public async Task EF10_Dapper_Execute_CrossTenantMethod_NoDiagnostic()
    {
        var source = @"
using System;
using System.Data;
using Dapper;
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
    [CrossTenant(""Admin cleanup"")]
    public void DoWork(IDbConnection conn)
    {
        conn.Execute(""DELETE FROM Customers WHERE IsDeleted = 1"");
    }
}
";
        await Verify.VerifyWithDapper10Async(source);
    }
}
