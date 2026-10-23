using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL009Tests
{
    // ===========================================================
    // Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task ExecuteSqlRaw_SetAppTenantId_Reports()
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
    private string _tid = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

class MyService
{
    void SetTenant(AppDbContext db, string t)
    {
        {|#0:db.Database.ExecuteSqlRaw(""SET app.tenant_id = @t"", t)|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL009)
            .WithLocation(0)
            .WithArguments("SET app.tenant_id");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task ExecuteSqlRaw_SetConfigFalse_Reports()
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
    private string _tid = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

class MyService
{
    void SetTenant(AppDbContext db, string t)
    {
        {|#0:db.Database.ExecuteSqlRaw(""SELECT set_config('app.tenant_id', @t, false)"", t)|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL009)
            .WithLocation(0)
            .WithArguments("set_config(..., false)");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task CommandText_SetMyappTenant_Reports()
    {
        var source = @"
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

class MyService
{
    void SetTenant(DbCommand cmd)
    {
        {|#0:cmd.CommandText = ""SET myapp.tenant = 'abc'""|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL009)
            .WithLocation(0)
            .WithArguments("SET myapp.tenant");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task ExecuteSqlRaw_SetLocalAppTenantId_NoReport()
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
    private string _tid = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

class MyService
{
    void SetTenant(AppDbContext db, string t)
    {
        db.Database.ExecuteSqlRaw(""SET LOCAL app.tenant_id = @t"", t);
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task ExecuteSqlRaw_SetConfigTrue_NoReport()
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
    private string _tid = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

class MyService
{
    void SetTenant(AppDbContext db, string t)
    {
        db.Database.ExecuteSqlRaw(""SELECT set_config('app.tenant_id', @t, true)"", t);
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task ExecuteSqlRaw_SetTransactionIsolation_NoReport()
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
    private string _tid = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

class MyService
{
    void SetIsolation(AppDbContext db)
    {
        db.Database.ExecuteSqlRaw(""SET TRANSACTION ISOLATION LEVEL READ COMMITTED"");
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task ExecuteSqlRaw_CrossTenant_NoReport()
    {
        var source = @"
using System;
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason = """") { }
}

class MyService
{
    [CrossTenant(""migration"")]
    void SetTenant(AppDbContext db, string t)
    {
        db.Database.ExecuteSqlRaw(""SET app.tenant_id = @t"", t);
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task ExecuteSqlRaw_NonConstantSql_NoReport()
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
    private string _tid = ""t1"";
    public DbSet<Order> Orders { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(e => e.TenantId == _tid);
    }
}

class MyService
{
    void SetTenant(AppDbContext db, string sql)
    {
        db.Database.ExecuteSqlRaw(sql);
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }
}
