using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL010Tests
{
    private const string SharedPreamble = @"
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.AspNetCore.Mvc
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ApiControllerAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class FromBodyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class FromFormAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class FromServicesAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class FromRouteAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class FromQueryAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class FromHeaderAttribute : Attribute { }
}

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class CustomerDto
{
    public int Id { get; set; }
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
";

    // ===========================================================
    // Positive — TL010 (Warning)
    // ===========================================================

    [Fact]
    public async Task FromBody_TenantEntity_ReportsTL010()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Create([FromBody] Customer {|#0:customer|})
    {
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL010)
            .WithLocation(0)
            .WithArguments("Customer", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task FromForm_TenantEntity_ReportsTL010()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Create([FromForm] Customer {|#0:customer|})
    {
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL010)
            .WithLocation(0)
            .WithArguments("Customer", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task ApiController_ImplicitBinding_TenantEntity_ReportsTL010()
    {
        var source = SharedPreamble + @"
[ApiController]
public class MyController
{
    public void Create(Customer {|#0:customer|})
    {
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL010)
            .WithLocation(0)
            .WithArguments("Customer", "TenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // Negative — no diagnostic
    // ===========================================================

    [Fact]
    public async Task FromBody_NonTenantDto_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Create([FromBody] CustomerDto dto)
    {
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task FromServices_TenantEntity_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Create([FromServices] Customer customer)
    {
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task FromRoute_PrimitiveType_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get([FromRoute] int customerId)
    {
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task CrossTenant_TenantEntity_NoDiagnostic()
    {
        var source = SharedPreamble + @"
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason = """") { }
}

public class MyController
{
    [CrossTenant(""Admin endpoint"")]
    public void Create([FromBody] Customer customer)
    {
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task NonApiController_NoExplicitBinding_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Create(Customer customer)
    {
    }
}
";
        // Not an [ApiController], no [FromBody]/[FromForm] → not body-bound
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task ApiController_GlobalEntity_NoDiagnostic()
    {
        var source = SharedPreamble + @"
[ApiController]
public class MyController
{
    public void Create(Customer customer)
    {
    }
}
";
        var editorconfig = @"
is_global = true
tenantlint.global_entities = Customer
";
        await Verify.VerifyWithEfCoreAndOptionsAsync(source, editorconfig);
    }
}
