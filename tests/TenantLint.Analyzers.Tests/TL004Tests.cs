using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL004Tests
{
    private const string SharedPreamble = @"
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

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

namespace Microsoft.AspNetCore.Http
{
    public interface IHeaderDictionary
    {
        string this[string key] { get; set; }
        bool TryGetValue(string key, out string value);
    }

    public interface IQueryCollection
    {
        string this[string key] { get; }
        bool TryGetValue(string key, out string value);
    }

    public interface IFormCollection
    {
        string this[string key] { get; }
        bool TryGetValue(string key, out string value);
    }

    public class HttpRequest
    {
        public IHeaderDictionary Headers { get; set; }
        public IQueryCollection Query { get; set; }
        public IFormCollection Form { get; set; }
    }
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
";

    // ===========================================================
    // Positive — TL004 (Warning)
    // ===========================================================

    [Fact]
    public async Task FromHeader_TenantId_ReportsTL004()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get([FromHeader] string {|#0:tenantId|})
    {
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL004)
            .WithLocation(0)
            .WithArguments("tenantId", "FromHeader");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task FromQuery_TenantId_CaseInsensitive_ReportsTL004()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get([FromQuery] string {|#0:TenantId|})
    {
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL004)
            .WithLocation(0)
            .WithArguments("TenantId", "FromQuery");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task FromRoute_TenantId_ReportsTL104()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get([FromRoute] string {|#0:tenantId|})
    {
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL104)
            .WithLocation(0)
            .WithArguments("tenantId");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task CustomPropertyName_FromQuery_ReportsTL004()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get([FromQuery] string {|#0:organizationId|})
    {
    }
}
";
        var editorconfig = @"
is_global = true
tenantlint.tenant_property_names = TenantId, OrganizationId
";
        var expected = Verify.Diagnostic(Descriptors.TL004)
            .WithLocation(0)
            .WithArguments("organizationId", "FromQuery");

        await Verify.VerifyWithEfCoreAndOptionsAsync(source, editorconfig, expected);
    }

    // ===========================================================
    // Negative — no diagnostic
    // ===========================================================

    [Fact]
    public async Task FromHeader_NonTenantName_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get([FromHeader] string authToken)
    {
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task FromServices_TenantId_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get([FromServices] string tenantId)
    {
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task NoBindingAttribute_TenantId_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(string tenantId)
    {
    }
}
";
        // No binding attribute → not client input
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task CrossTenant_TenantId_NoDiagnostic()
    {
        var source = SharedPreamble + @"
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason = """") { }
}

public class MyController
{
    [CrossTenant(""Admin lookup by tenant"")]
    public void Get([FromHeader] string tenantId)
    {
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task FromBody_TenantId_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get([FromBody] string tenantId)
    {
    }
}
";
        // [FromBody] is not a client-input binding source for TL004
        // (body binding for entity types is handled by TL010)
        await Verify.VerifyWithEfCoreAsync(source);
    }

    // ===========================================================
    // Direct header/query reads — Positive
    // ===========================================================

    [Fact]
    public async Task Headers_Indexer_XTenantId_ReportsTL004()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(HttpRequest request)
    {
        var tid = {|#0:request.Headers[""X-Tenant-Id""]|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL004)
            .WithLocation(0)
            .WithArguments("X-Tenant-Id", "Request.Headers");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task Headers_TryGetValue_XTenantId_ReportsTL004()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(HttpRequest request)
    {
        {|#0:request.Headers.TryGetValue(""X-Tenant-Id"", out var value)|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL004)
            .WithLocation(0)
            .WithArguments("X-Tenant-Id", "Request.Headers");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task Query_Indexer_TenantId_ReportsTL004()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(HttpRequest request)
    {
        var tid = {|#0:request.Query[""tenantId""]|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL004)
            .WithLocation(0)
            .WithArguments("tenantId", "Request.Query");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task Query_TryGetValue_TenantId_ReportsTL004()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(HttpRequest request)
    {
        {|#0:request.Query.TryGetValue(""tenantId"", out var value)|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL004)
            .WithLocation(0)
            .WithArguments("tenantId", "Request.Query");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task Form_Indexer_TenantId_ReportsTL004()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(HttpRequest request)
    {
        var tid = {|#0:request.Form[""tenantId""]|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL004)
            .WithLocation(0)
            .WithArguments("tenantId", "Request.Form");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task Form_TryGetValue_TenantId_ReportsTL004()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(HttpRequest request)
    {
        {|#0:request.Form.TryGetValue(""tenantId"", out var value)|};
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL004)
            .WithLocation(0)
            .WithArguments("tenantId", "Request.Form");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // Direct header/query/form reads — Negative
    // ===========================================================

    [Fact]
    public async Task Form_Indexer_NonTenantKey_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(HttpRequest request)
    {
        var name = request.Form[""username""];
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task Query_TryGetValue_NonTenantKey_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(HttpRequest request)
    {
        request.Query.TryGetValue(""page"", out var value);
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    // ===========================================================
    // Direct header/query reads — Negative
    // ===========================================================

    [Fact]
    public async Task Headers_Indexer_Authorization_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(HttpRequest request)
    {
        var auth = request.Headers[""Authorization""];
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task Query_Indexer_Page_NoDiagnostic()
    {
        var source = SharedPreamble + @"
public class MyController
{
    public void Get(HttpRequest request)
    {
        var page = request.Query[""page""];
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task Headers_Indexer_CrossTenant_NoDiagnostic()
    {
        var source = SharedPreamble + @"
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class CrossTenantAttribute : Attribute
{
    public CrossTenantAttribute(string reason = """") { }
}

public class MyController
{
    [CrossTenant(""Admin lookup"")]
    public void Get(HttpRequest request)
    {
        var tid = request.Headers[""X-Tenant-Id""];
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }
}
