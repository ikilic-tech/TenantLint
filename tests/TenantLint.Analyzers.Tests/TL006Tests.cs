using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL006Tests
{
    // Usings must come first, then namespace stubs, then test types
    private const string Preamble = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using System.Linq;

namespace Microsoft.Extensions.Caching.Memory
{
    public interface IMemoryCache
    {
        object Get(string key);
        void Set(string key, object value);
        bool TryGetValue(string key, out object value);
        T GetOrCreate<T>(string key, System.Func<object, T> factory);
        System.Threading.Tasks.Task<T> GetOrCreateAsync<T>(string key, System.Func<object, System.Threading.Tasks.Task<T>> factory);
    }
}

namespace Microsoft.Extensions.Caching.Distributed
{
    public interface IDistributedCache
    {
        byte[] Get(string key);
        System.Threading.Tasks.Task<byte[]> GetAsync(string key);
        void Set(string key, byte[] value);
        System.Threading.Tasks.Task SetAsync(string key, byte[] value);
        string GetString(string key);
        System.Threading.Tasks.Task<string> GetStringAsync(string key);
        void SetString(string key, string value);
        System.Threading.Tasks.Task SetStringAsync(string key, string value);
    }
}

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
";

    // ===========================================================
    // Positive Cases (3+)
    // ===========================================================

    [Fact]
    public async Task MemoryCache_ConstantKey_TouchesTenantData_Reports()
    {
        var source = Preamble + @"
class MyService
{
    private readonly IMemoryCache _cache;
    private readonly AppDbContext _db;

    void DoWork()
    {
        _cache.Set({|#0:""orders""|}, _db.Orders.ToList());
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL006)
            .WithLocation(0)
            .WithArguments("orders");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task MemoryCache_GetOrCreate_TouchesTenantData_Reports()
    {
        var source = Preamble + @"
class MyService
{
    private readonly IMemoryCache _cache;
    private readonly AppDbContext _db;

    void DoWork()
    {
        _cache.GetOrCreate({|#0:""report""|}, entry => _db.Orders.ToList());
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL006)
            .WithLocation(0)
            .WithArguments("report");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task DistributedCache_SetString_TouchesTenantData_Reports()
    {
        var source = Preamble + @"
class MyService
{
    private readonly IDistributedCache _distCache;
    private readonly AppDbContext _db;

    void DoWork()
    {
        var data = _db.Orders.ToList().ToString();
        _distCache.SetString({|#0:""key""|}, data);
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL006)
            .WithLocation(0)
            .WithArguments("key");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task MemoryCache_KeyIncludesTenantId_NoReport()
    {
        var source = Preamble + @"
class MyService
{
    private readonly IMemoryCache _cache;
    private readonly AppDbContext _db;
    private string _tenantId;

    void DoWork()
    {
        _cache.Set($""orders:{_tenantId}"", _db.Orders.ToList());
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task MemoryCache_ConstantKey_NoTenantData_NoReport()
    {
        var source = Preamble + @"
class MyService
{
    private readonly IMemoryCache _cache;

    void DoWork()
    {
        _cache.Set(""config"", ""some value"");
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task MemoryCache_CrossTenant_NoReport()
    {
        var source = Preamble + @"
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
class CrossTenantAttribute : System.Attribute
{
    public CrossTenantAttribute(string reason = """") { }
}

class MyService
{
    private readonly IMemoryCache _cache;
    private readonly AppDbContext _db;

    [CrossTenant(""admin cache"")]
    void DoWork()
    {
        _cache.Set(""orders"", _db.Orders.ToList());
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task MemoryCache_NonConstantKey_NoReport()
    {
        var source = Preamble + @"
class MyService
{
    private readonly IMemoryCache _cache;
    private readonly AppDbContext _db;

    string GetKey() => ""key"";

    void DoWork()
    {
        _cache.Set(GetKey(), _db.Orders.ToList());
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task NoCacheTypes_NoReport()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;
using System.Linq;

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
    private readonly AppDbContext _db;

    void DoWork()
    {
        var data = _db.Orders.ToList();
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }
}
