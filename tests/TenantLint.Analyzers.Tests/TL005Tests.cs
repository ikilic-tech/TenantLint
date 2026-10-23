using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using TenantLint.CodeFixes;
using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;
using CodeFixVerify = CSharpCodeFixVerifier<TenantIsolationAnalyzer, TL005CodeFixProvider>;

public class TL005Tests
{
    // ===========================================================
    // Positive — Warning (TL005)
    // ===========================================================

    [Fact]
    public async Task UnfilteredTenantEntity_ReportsTL005()
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
    public DbSet<Customer> {|#0:Customers|} { get; set; }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL005)
            .WithLocation(0)
            .WithArguments("Customer", "TenantId", "AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task UnfilteredTenantEntity_WithNonTenantFilter_ReportsTL005()
    {
        // HasQueryFilter exists but does NOT reference the tenant property
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
    public DbSet<Customer> {|#0:Customers|} { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => !e.IsDeleted);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL005)
            .WithLocation(0)
            .WithArguments("Customer", "TenantId", "AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task MultipleUnfilteredEntities_ReportsForEach()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> {|#0:Customers|} { get; set; }
    public DbSet<Order> {|#1:Orders|} { get; set; }
}
";
        var expectedCustomer = Verify.Diagnostic(Descriptors.TL005)
            .WithLocation(0)
            .WithArguments("Customer", "TenantId", "AppDbContext");

        var expectedOrder = Verify.Diagnostic(Descriptors.TL005)
            .WithLocation(1)
            .WithArguments("Order", "TenantId", "AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expectedCustomer, expectedOrder);
    }

    [Fact]
    public async Task CustomTenantProperty_ViaEditorConfig_ReportsTL005()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string OrganizationId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> {|#0:Customers|} { get; set; }
}
";
        var editorconfig = @"
is_global = true
tenantlint.tenant_property_names = OrganizationId
";
        var expected = Verify.Diagnostic(Descriptors.TL005)
            .WithLocation(0)
            .WithArguments("Customer", "OrganizationId", "AppDbContext");

        await Verify.VerifyWithEfCoreAndOptionsAsync(source, editorconfig, expected);
    }

    // ===========================================================
    // Positive — Info (TL105) — dynamic filter
    // ===========================================================

    [Fact]
    public async Task DynamicFilterLoop_ReportsTL105()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class {|#1:AppDbContext|} : DbContext
{
    public DbSet<Customer> {|#0:Customers|} { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(
                BuildFilterExpression(entityType.ClrType));
        }
    }

    private static LambdaExpression BuildFilterExpression(Type type) => throw new NotImplementedException();
}
";
        var expectedTL105 = Verify.Diagnostic(Descriptors.TL105)
            .WithLocation(0)
            .WithArguments("Customer", "TenantId", "AppDbContext");

        var expectedTL900 = Verify.Diagnostic("TL900")
            .WithLocation(1)
            .WithArguments("AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expectedTL105, expectedTL900);
    }

    // ===========================================================
    // Negative — no diagnostic
    // ===========================================================

    [Fact]
    public async Task FilteredTenantEntity_NoDiagnostic()
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
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task InheritedFilter_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class BaseEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class Customer : BaseEntity
{
    public string Name { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<BaseEntity> BaseEntities { get; set; }
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseEntity>().HasQueryFilter(e => e.TenantId == _tid);
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task IEntityTypeConfiguration_WithFilter_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasQueryFilter(e => e.TenantId == ""t1"");
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task EF10_NamedFilter_NoDiagnostic()
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
";
        await Verify.VerifyWithEfCore10Async(source);
    }

    [Fact]
    public async Task GlobalEntity_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class AuditLog
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<AuditLog> AuditLogs { get; set; }
}
";
        var editorconfig = @"
is_global = true
tenantlint.global_entities = AuditLog
";
        await Verify.VerifyWithEfCoreAndOptionsAsync(source, editorconfig);
    }

    [Fact]
    public async Task NoTenantProperty_NoDiagnostic()
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
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task NoDbContext_NoDiagnostic()
    {
        var source = @"
public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    // ===========================================================
    // Code Fix Tests
    // ===========================================================

    [Fact]
    public async Task CodeFix_AddsHasQueryFilter_ToExistingOnModelCreating()
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
    public DbSet<Customer> {|#0:Customers|} { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
";

        var fixedSource = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == TODO_SET_TENANT_ACCESSOR);
    }
}
";

        // The stub uses TODO_SET_TENANT_ACCESSOR which won't resolve as a real filter,
        // so TL005 keeps firing. Limit to 1 iteration to prevent duplicate application.
        var test = new CSharpCodeFixTest<TenantIsolationAnalyzer, TL005CodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = Verify.Net80WithEfCore,
            CodeActionEquivalenceKey = "TL005_AddFilter",
            CompilerDiagnostics = CompilerDiagnostics.None,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipFixAllCheck
                | CodeFixTestBehaviors.SkipFixAllInDocumentCheck,
        };
        test.TestBehaviors = TestBehaviors.SkipSuppressionCheck;
        test.DisabledDiagnostics.Add("TL111");
        test.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithLocation(0)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        test.FixedState.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithSpan(12, 28, 12, 37)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CodeFix_CreatesOnModelCreating_WhenMissing()
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
    public DbSet<Customer> {|#0:Customers|} { get; set; }
}
";

        var fixedSource = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == TODO_SET_TENANT_ACCESSOR);
    }
}
";

        var test = new CSharpCodeFixTest<TenantIsolationAnalyzer, TL005CodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = Verify.Net80WithEfCore,
            CodeActionEquivalenceKey = "TL005_AddFilter",
            CompilerDiagnostics = CompilerDiagnostics.None,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipFixAllCheck
                | CodeFixTestBehaviors.SkipFixAllInDocumentCheck,
        };
        test.TestBehaviors = TestBehaviors.SkipSuppressionCheck;
        test.DisabledDiagnostics.Add("TL111");
        test.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithLocation(0)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        test.FixedState.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithSpan(12, 28, 12, 37)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CodeFix_UsesActualParameterName_WhenDifferent()
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
    public DbSet<Customer> {|#0:Customers|} { get; set; }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
    }
}
";

        var fixedSource = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Customer>().HasQueryFilter(e => e.TenantId == TODO_SET_TENANT_ACCESSOR);
    }
}
";

        var test = new CSharpCodeFixTest<TenantIsolationAnalyzer, TL005CodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = Verify.Net80WithEfCore,
            CodeActionEquivalenceKey = "TL005_AddFilter",
            CompilerDiagnostics = CompilerDiagnostics.None,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipFixAllCheck
                | CodeFixTestBehaviors.SkipFixAllInDocumentCheck,
        };
        test.TestBehaviors = TestBehaviors.SkipSuppressionCheck;
        test.DisabledDiagnostics.Add("TL111");
        test.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithLocation(0)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        test.FixedState.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithSpan(12, 28, 12, 37)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CodeFix_GeneratesNamedFilter_WhenContextUsesNamedFilters()
    {
        // Order already has a named filter → code fix for Invoice should use named syntax
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class Invoice
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Order> Orders { get; set; }
    public DbSet<Invoice> {|#0:Invoices|} { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
    }
}
";

        var fixedSource = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class Invoice
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tid = ""t1"";
    public DbSet<Order> Orders { get; set; }
    public DbSet<Invoice> Invoices { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasQueryFilter(""Tenant"", e => e.TenantId == _tid);
        modelBuilder.Entity<Invoice>().HasQueryFilter(""Tenant"", x => x.TenantId == TODO_SET_TENANT_ACCESSOR);
    }
}
";

        var test = new CSharpCodeFixTest<TenantIsolationAnalyzer, TL005CodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = Verify.Net100WithEfCore10,
            CodeActionEquivalenceKey = "TL005_AddFilter",
            CompilerDiagnostics = CompilerDiagnostics.None,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipFixAllCheck
                | CodeFixTestBehaviors.SkipFixAllInDocumentCheck,
        };
        test.TestBehaviors = TestBehaviors.SkipSuppressionCheck;
        test.DisabledDiagnostics.Add("TL111");
        test.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithLocation(0)
                .WithArguments("Invoice", "TenantId", "AppDbContext"));
        test.FixedState.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithSpan(20, 27, 20, 35)
                .WithArguments("Invoice", "TenantId", "AppDbContext"));
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CodeFix_GeneratesUnnamedFilter_WhenNoNamedFilters()
    {
        // EF 8 context with no named filters — should use unnamed syntax (existing behavior)
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> {|#0:Customers|} { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
";

        var fixedSource = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == TODO_SET_TENANT_ACCESSOR);
    }
}
";

        var test = new CSharpCodeFixTest<TenantIsolationAnalyzer, TL005CodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = Verify.Net80WithEfCore,
            CodeActionEquivalenceKey = "TL005_AddFilter",
            CompilerDiagnostics = CompilerDiagnostics.None,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipFixAllCheck
                | CodeFixTestBehaviors.SkipFixAllInDocumentCheck,
        };
        test.TestBehaviors = TestBehaviors.SkipSuppressionCheck;
        test.DisabledDiagnostics.Add("TL111");
        test.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithLocation(0)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        test.FixedState.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithSpan(12, 28, 12, 37)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CodeFix_AvoidsLambdaCollision_WhenEIsInScope()
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
    public DbSet<Customer> {|#0:Customers|} { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = ""something"";
        System.Console.WriteLine(e);
    }
}
";

        var fixedSource = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = ""something"";
        System.Console.WriteLine(e);
        modelBuilder.Entity<Customer>().HasQueryFilter(x => x.TenantId == TODO_SET_TENANT_ACCESSOR);
    }
}
";

        var test = new CSharpCodeFixTest<TenantIsolationAnalyzer, TL005CodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = Verify.Net80WithEfCore,
            CodeActionEquivalenceKey = "TL005_AddFilter",
            CompilerDiagnostics = CompilerDiagnostics.None,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipFixAllCheck
                | CodeFixTestBehaviors.SkipFixAllInDocumentCheck,
        };
        test.TestBehaviors = TestBehaviors.SkipSuppressionCheck;
        test.DisabledDiagnostics.Add("TL111");
        test.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithLocation(0)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        test.FixedState.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithSpan(12, 28, 12, 37)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CodeFix_UsesConfiguredFilterName_ViaEditorConfig()
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
    public DbSet<Customer> {|#0:Customers|} { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
";

        var fixedSource = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Customer>().HasQueryFilter(""Isolation"", e => e.TenantId == TODO_SET_TENANT_ACCESSOR);
    }
}
";

        var editorconfig = @"
is_global = true
tenantlint.tenant_filter_name = Isolation
";

        var test = new CSharpCodeFixTest<TenantIsolationAnalyzer, TL005CodeFixProvider, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = Verify.Net80WithEfCore,
            CodeActionEquivalenceKey = "TL005_AddFilter",
            CompilerDiagnostics = CompilerDiagnostics.None,
            CodeFixTestBehaviors = CodeFixTestBehaviors.SkipFixAllCheck
                | CodeFixTestBehaviors.SkipFixAllInDocumentCheck,
        };
        test.TestBehaviors = TestBehaviors.SkipSuppressionCheck;
        test.DisabledDiagnostics.Add("TL111");
        test.TestState.AnalyzerConfigFiles.Add(("/.editorconfig", editorconfig));
        test.FixedState.AnalyzerConfigFiles.Add(("/.editorconfig", editorconfig));
        test.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithLocation(0)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        test.FixedState.ExpectedDiagnostics.Add(
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithSpan(12, 28, 12, 37)
                .WithArguments("Customer", "TenantId", "AppDbContext"));
        await test.RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CodeFix_NotOffered_WhenEntityHasIEntityTypeConfiguration()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> {|#0:Customers|} { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        // No tenant filter — TL005 fires, but code fix should NOT be offered
        // because the configuration belongs in the IEntityTypeConfiguration, not OnModelCreating
    }
}
";

        var expected = new[]
        {
            CodeFixVerify.Diagnostic(Descriptors.TL005)
                .WithLocation(0)
                .WithArguments("Customer", "TenantId", "AppDbContext")
        };

        // FixedSource == source means no fix is applied
        await CodeFixVerify.VerifyCodeFixAsync(source, expected, source);
    }
}
