using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL901TL902Tests
{
    // ===========================================================
    // TL901 — [CrossTenant] should provide a reason
    // ===========================================================

    [Fact]
    public async Task TL901_NoArgs_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute() { }
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class MyService
{
    [{|#0:CrossTenant|}]
    public void DoWork(AppDbContext db) { }
}
";
        var expected = Verify.Diagnostic("TL901")
            .WithLocation(0)
            .WithArguments("DoWork");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task TL901_EmptyString_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class MyService
{
    [{|#0:CrossTenant("""")|}]
    public void DoWork(AppDbContext db) { }
}
";
        var expected = Verify.Diagnostic("TL901")
            .WithLocation(0)
            .WithArguments("DoWork");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task TL901_Whitespace_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class MyService
{
    [{|#0:CrossTenant(""  "")|}]
    public void DoWork(AppDbContext db) { }
}
";
        var expected = Verify.Diagnostic("TL901")
            .WithLocation(0)
            .WithArguments("DoWork");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task TL901_TODO_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class MyService
{
    [{|#0:CrossTenant(""TODO"")|}]
    public void DoWork(AppDbContext db) { }
}
";
        var expected = Verify.Diagnostic("TL901")
            .WithLocation(0)
            .WithArguments("DoWork");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task TL901_TodoReason_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class MyService
{
    [{|#0:CrossTenant(""TODO: reason"")|}]
    public void DoWork(AppDbContext db) { }
}
";
        var expected = Verify.Diagnostic("TL901")
            .WithLocation(0)
            .WithArguments("DoWork");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task TL901_PlaceholderReason_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class MyService
{
    [{|#0:CrossTenant(""reason"")|}]
    public void DoWork(AppDbContext db) { }
}
";
        var expected = Verify.Diagnostic("TL901")
            .WithLocation(0)
            .WithArguments("DoWork");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task TL901_NA_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class MyService
{
    [{|#0:CrossTenant(""n/a"")|}]
    public void DoWork(AppDbContext db) { }
}
";
        var expected = Verify.Diagnostic("TL901")
            .WithLocation(0)
            .WithArguments("DoWork");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task TL901_ValidReason_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class MyService
{
    [CrossTenant(""Admin migration — needs all tenants"")]
    public void DoWork(AppDbContext db) { }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    // ===========================================================
    // TL902 — Class-level [CrossTenant] suppresses all methods
    // ===========================================================

    [Fact]
    public async Task TL902_ClassLevelAttribute_Reports()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

[{|#0:CrossTenant(""Admin service"")|}]
public class MyService
{
    public void DoWork(AppDbContext db) { }
}
";
        var expected = Verify.Diagnostic("TL902")
            .WithLocation(0)
            .WithArguments("MyService");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task TL902_MethodLevelAttribute_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class MyService
{
    [CrossTenant(""Admin migration"")]
    public void DoWork(AppDbContext db) { }
}
";
        // Method-level attribute should not produce TL902
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task TL902_ClassLevel_WithPlaceholderReason_ReportsBothTL901AndTL902()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason) { }
}

public class Customer
{
    public int Id { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

[{|#0:CrossTenant(""TODO"")|}]
public class MyService
{
    public void DoWork(AppDbContext db) { }
}
";
        var expectedTL902 = Verify.Diagnostic("TL902")
            .WithLocation(0)
            .WithArguments("MyService");

        var expectedTL901 = Verify.Diagnostic("TL901")
            .WithLocation(0)
            .WithArguments("MyService");

        await Verify.VerifyWithEfCoreAsync(source, expectedTL902, expectedTL901);
    }
}
