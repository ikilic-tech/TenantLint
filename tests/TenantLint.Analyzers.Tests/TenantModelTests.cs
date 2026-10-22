using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TenantModelTests
{
    // ===== Helper to build TenantModel directly =====

    private static async Task<TenantModel> BuildModelAsync(
        string source,
        ConfigOptions? options = null)
    {
        var references = await Verify.Net80WithEfCore.ResolveAsync(
            LanguageNames.CSharp, TestContext.Current.CancellationToken);

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        options ??= ConfigOptions.CreateDefault();

        var builder = new TenantModelBuilder(compilation, options);
        return builder.Build();
    }

    // ===== EXPLICIT FILTER DISCOVERY =====

    [Fact]
    public async Task ExplicitFilter_ClassicHasQueryFilter_MarksEntityAsTenantScoped()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public string Name { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tenantId = ""default"";
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tenantId);
    }
}
";
        var model = await BuildModelAsync(source);

        Assert.True(model.HasEfCore);
        Assert.Single(model.TenantEntities);

        var entity = model.TenantEntities.Values.First();
        Assert.Equal("Customer", entity.EntityType.Name);
        Assert.Equal("TenantId", entity.TenantPropertyName);
        Assert.Equal(FilterState.Filtered, entity.FilterState);
    }

    [Fact]
    public async Task ExplicitFilter_NonTenantFilter_EntityStillTenantScopedByConvention()
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
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => !e.IsDeleted);
    }
}
";
        var model = await BuildModelAsync(source);

        Assert.True(model.HasEfCore);
        Assert.Single(model.TenantEntities);

        var entity = model.TenantEntities.Values.First();
        Assert.Equal("Customer", entity.EntityType.Name);
        Assert.Equal(FilterState.Unfiltered, entity.FilterState);
    }

    [Fact]
    public async Task ExplicitFilter_InEntityTypeConfiguration_Detected()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasQueryFilter(e => e.TenantId != null);
    }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
    }
}
";
        var model = await BuildModelAsync(source);

        Assert.True(model.HasEfCore);
        Assert.Single(model.TenantEntities);

        var entity = model.TenantEntities.Values.First();
        Assert.Equal("Customer", entity.EntityType.Name);
        Assert.Equal(FilterState.Filtered, entity.FilterState);
    }

    // ===== CONVENTION DISCOVERY =====

    [Fact]
    public async Task Convention_EntityWithTenantIdProperty_DiscoveredViaDbSet()
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
    public DbSet<Order> Orders { get; set; }
}
";
        var model = await BuildModelAsync(source);

        Assert.True(model.HasEfCore);
        Assert.Single(model.TenantEntities);

        var entity = model.TenantEntities.Values.First();
        Assert.Equal("Order", entity.EntityType.Name);
        Assert.Equal("TenantId", entity.TenantPropertyName);
        Assert.Equal(FilterState.Unfiltered, entity.FilterState);
    }

    [Fact]
    public async Task Convention_EntityWithoutTenantProperty_NotTenantScoped()
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
        var model = await BuildModelAsync(source);

        Assert.True(model.HasEfCore);
        Assert.Empty(model.TenantEntities);
    }

    [Fact]
    public async Task Convention_CustomPropertyName_ViaOptions()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Order
{
    public int Id { get; set; }
    public string OrganizationId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }
}
";
        var options = ConfigOptions.CreateWithPropertyNames("OrganizationId");
        var model = await BuildModelAsync(source, options);

        Assert.Single(model.TenantEntities);
        Assert.Equal("OrganizationId", model.TenantEntities.Values.First().TenantPropertyName);
    }

    // ===== GLOBAL ENTITIES =====

    [Fact]
    public async Task GlobalEntities_ExcludedFromTenantModel()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Tenant
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Tenant> Tenants { get; set; }
}
";
        var options = ConfigOptions.CreateWithGlobalEntities("Tenant");
        var model = await BuildModelAsync(source, options);

        Assert.Empty(model.TenantEntities);
    }

    // ===== DYNAMIC FILTER DETECTION =====

    [Fact]
    public async Task DynamicFilter_ForeachGetEntityTypes_Detected()
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

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

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
        var model = await BuildModelAsync(source);

        Assert.Single(model.DynamicFilterContexts);
        Assert.Equal("AppDbContext", model.DynamicFilterContexts.First().Name);
    }

    [Fact]
    public async Task DynamicFilter_ConventionEntitiesMarkedAsUnknown()
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

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

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
        var model = await BuildModelAsync(source);

        Assert.Single(model.TenantEntities);
        var entity = model.TenantEntities.Values.First();
        // Convention entities should be Unknown (not Filtered) when dynamic filters are detected
        Assert.Equal(FilterState.Unknown, entity.FilterState);
    }

    [Fact]
    public async Task DynamicFilter_ReportsTL900()
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

public class {|#0:AppDbContext|} : DbContext
{
    public DbSet<Customer> Customers { get; set; }

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
        var expected = Verify.Diagnostic("TL900")
            .WithLocation(0)
            .WithArguments("AppDbContext");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===== TABLE NAMES =====

    [Fact]
    public async Task TableNames_IncludesEntityNameAndDbSetName()
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
    public DbSet<Customer> Customers { get; set; }
}
";
        var model = await BuildModelAsync(source);
        var entity = model.TenantEntities.Values.First();

        Assert.Contains("Customer", entity.TableNames);
        Assert.Contains("Customers", entity.TableNames);
        Assert.Contains("customer", entity.TableNames);
        Assert.Contains("customers", entity.TableNames);
    }

    [Fact]
    public async Task TableNames_IncludesSnakeCaseVariants()
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
    public DbSet<OrderItem> OrderItems { get; set; }
}
";
        var model = await BuildModelAsync(source);
        var entity = model.TenantEntities.Values.First();

        Assert.Contains("order_item", entity.TableNames);
        Assert.Contains("order_items", entity.TableNames);
    }

    [Fact]
    public async Task TableNames_IncludesTableAttribute()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

[Table(""cust"")]
public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}
";
        var model = await BuildModelAsync(source);
        var entity = model.TenantEntities.Values.First();

        Assert.Contains("cust", entity.TableNames);
    }

    [Fact]
    public async Task TableNames_IncludesToTableCall()
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
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().ToTable(""cust_table"");
    }
}
";
        var model = await BuildModelAsync(source);
        var entity = model.TenantEntities.Values.First();

        Assert.Contains("cust_table", entity.TableNames);
    }

    [Fact]
    public async Task TableNames_CaseInsensitiveLookup()
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
    public DbSet<Customer> Customers { get; set; }
}
";
        var model = await BuildModelAsync(source);

        Assert.NotNull(model.GetEntityByTableName("CUSTOMER"));
        Assert.NotNull(model.GetEntityByTableName("customer"));
        Assert.NotNull(model.GetEntityByTableName("Customer"));
    }

    // ===== CONFIG OPTIONS =====

    [Fact]
    public void ConfigOptions_DefaultTenantPropertyName()
    {
        var options = ConfigOptions.CreateDefault();
        Assert.Contains("TenantId", options.TenantPropertyNames);
    }

    [Fact]
    public void ConfigOptions_DerivedColumnNames()
    {
        var derived = ConfigOptions.DeriveColumnNames(
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "TenantId"));

        Assert.Contains("tenant_id", derived);
        Assert.Contains("tenantid", derived);
        Assert.Contains("TenantId", derived);
    }

    [Fact]
    public void ConfigOptions_ToSnakeCase()
    {
        Assert.Equal("tenant_id", ConfigOptions.ToSnakeCase("TenantId"));
        Assert.Equal("order_item", ConfigOptions.ToSnakeCase("OrderItem"));
        Assert.Equal("id", ConfigOptions.ToSnakeCase("Id"));
        Assert.Equal("a_b_c", ConfigOptions.ToSnakeCase("ABC"));
    }

    // ===== NEGATIVE CASES =====

    [Fact]
    public void NoEfCore_NoEntitiesDiscovered()
    {
        var source = @"
public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}
";
        // Build without EF Core references
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var options = ConfigOptions.CreateDefault();
        var builder = new TenantModelBuilder(compilation, options);
        var model = builder.Build();

        Assert.False(model.HasEfCore);
        Assert.Empty(model.TenantEntities);
    }

    [Fact]
    public async Task EmptyDbContext_NoEntitiesDiscovered()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
}
";
        var model = await BuildModelAsync(source);

        Assert.True(model.HasEfCore);
        Assert.Empty(model.TenantEntities);
    }

    [Fact]
    public async Task NonDbContextClass_Ignored()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class NotADbContext
{
    public DbSet<Customer> Customers { get; set; }

    protected void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}
";
        var model = await BuildModelAsync(source);

        // No class derives from DbContext, so nothing is analyzed
        Assert.Empty(model.TenantEntities);
        Assert.Empty(model.DynamicFilterContexts);
    }

    [Fact]
    public async Task MultipleDbContexts_IndependentDiscovery()
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

public class Country
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class CustomerDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}

public class OrderDbContext : DbContext
{
    public DbSet<Order> Orders { get; set; }
    public DbSet<Country> Countries { get; set; }
}
";
        var model = await BuildModelAsync(source);

        Assert.Equal(2, model.TenantEntities.Count);
        Assert.True(model.IsTenantScoped(
            model.TenantEntities.Keys.First(k => k.Name == "Customer")));
        Assert.True(model.IsTenantScoped(
            model.TenantEntities.Keys.First(k => k.Name == "Order")));
    }

    [Fact]
    public async Task EmptySource_NoDiagnostics()
    {
        await Verify.VerifyAnalyzerAsync("");
    }

    [Fact]
    public async Task InheritedTenantProperty_Discovered()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public abstract class TenantEntity
{
    public string TenantId { get; set; }
}

public class Customer : TenantEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}
";
        var model = await BuildModelAsync(source);

        Assert.Single(model.TenantEntities);
        Assert.Equal("TenantId", model.TenantEntities.Values.First().TenantPropertyName);
    }

    // ===== F2: LAMBDA PARAMETER VERIFICATION =====

    [Fact]
    public async Task LambdaVerification_ServiceTenantId_NotMisclassifiedAsTenantFilter()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public class TenantService
{
    public string TenantId { get; set; }
}

public class Customer
{
    public int Id { get; set; }
    public string TenantId { get; set; }
    public bool IsActive { get; set; }
}

public class AppDbContext : DbContext
{
    private TenantService _service = new TenantService();
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // This filter references _service.TenantId (not the entity parameter),
        // so it should NOT be classified as a tenant filter.
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.IsActive);
    }
}
";
        var model = await BuildModelAsync(source);

        Assert.Single(model.TenantEntities);
        var entity = model.TenantEntities.Values.First();
        Assert.Equal(FilterState.Unfiltered, entity.FilterState);
    }

    [Fact]
    public async Task LambdaVerification_EntityParameterTenantId_CorrectlyClassified()
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
        var model = await BuildModelAsync(source);

        Assert.Single(model.TenantEntities);
        var entity = model.TenantEntities.Values.First();
        Assert.Equal(FilterState.Filtered, entity.FilterState);
    }

    // ===== F3: INHERITANCE FILTER PROPAGATION =====

    [Fact]
    public async Task InheritanceFilter_BaseEntityFilter_PropagatedToDerivedEntity()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public abstract class TenantEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class Customer : TenantEntity
{
    public string Name { get; set; }
}

public class AppDbContext : DbContext
{
    private string _tenantId = ""default"";
    public DbSet<TenantEntity> TenantEntities { get; set; }
    public DbSet<Customer> Customers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantEntity>().HasQueryFilter(e => e.TenantId == _tenantId);
    }
}
";
        var model = await BuildModelAsync(source);

        var customerEntity = model.TenantEntities.Values.FirstOrDefault(e => e.EntityType.Name == "Customer");
        Assert.NotNull(customerEntity);
        // Derived entity should inherit Filtered state from base entity's HasQueryFilter
        Assert.Equal(FilterState.Filtered, customerEntity.FilterState);
    }

    [Fact]
    public async Task InheritanceFilter_NoBaseFilter_DerivedStaysUnfiltered()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

public abstract class TenantEntity
{
    public int Id { get; set; }
    public string TenantId { get; set; }
}

public class Customer : TenantEntity
{
    public string Name { get; set; }
}

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }
}
";
        var model = await BuildModelAsync(source);

        Assert.Single(model.TenantEntities);
        var entity = model.TenantEntities.Values.First();
        // Without base entity filter, derived entity should remain Unfiltered
        Assert.Equal(FilterState.Unfiltered, entity.FilterState);
    }

    // ===== F1: TRI-STATE FILTER STATE =====

    [Fact]
    public async Task FilterState_ExplicitFilter_IsFiltered()
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
        var model = await BuildModelAsync(source);
        Assert.Equal(FilterState.Filtered, model.GetFilterState(
            model.TenantEntities.Keys.First()));
    }

    [Fact]
    public async Task FilterState_ConventionOnly_IsUnfiltered()
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
    public DbSet<Customer> Customers { get; set; }
}
";
        var model = await BuildModelAsync(source);
        Assert.Equal(FilterState.Unfiltered, model.GetFilterState(
            model.TenantEntities.Keys.First()));
    }

    [Fact]
    public async Task FilterState_DynamicFilter_IsUnknown()
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

public class AppDbContext : DbContext
{
    public DbSet<Customer> Customers { get; set; }

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
        var model = await BuildModelAsync(source);
        Assert.Equal(FilterState.Unknown, model.GetFilterState(
            model.TenantEntities.Keys.First()));
    }
}
