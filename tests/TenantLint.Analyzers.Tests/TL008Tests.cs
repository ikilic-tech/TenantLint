using Xunit;

namespace TenantLint.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<TenantIsolationAnalyzer>;

public class TL008Tests
{
    private const string Preamble = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting
{
    public abstract class BackgroundService
    {
        protected abstract System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken);
    }

    public interface IHostedService
    {
        System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken);
        System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken);
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    public interface IServiceScopeFactory
    {
        IServiceScope CreateScope();
    }

    public interface IServiceScope : System.IDisposable
    {
        System.IServiceProvider ServiceProvider { get; }
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
    public async Task BackgroundService_WithDbContext_NoScope_Reports()
    {
        var source = Preamble + @"
class {|#0:OrderProcessor|} : BackgroundService
{
    private readonly AppDbContext _db;
    public OrderProcessor(AppDbContext db) { _db = db; }

    protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken ct)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL008)
            .WithLocation(0)
            .WithArguments("OrderProcessor");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task IHostedService_WithDbContext_NoScope_Reports()
    {
        var source = Preamble + @"
class {|#0:MyJob|} : IHostedService
{
    private readonly AppDbContext _db;
    public MyJob(AppDbContext db) { _db = db; }

    public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken ct) =>
        System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken ct) =>
        System.Threading.Tasks.Task.CompletedTask;
}
";
        var expected = Verify.Diagnostic(Descriptors.TL008)
            .WithLocation(0)
            .WithArguments("MyJob");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    [Fact]
    public async Task BackgroundService_DbContextProperty_NoScope_Reports()
    {
        var source = Preamble + @"
class {|#0:Worker|} : BackgroundService
{
    public AppDbContext Db { get; set; }

    protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken ct)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
";
        var expected = Verify.Diagnostic(Descriptors.TL008)
            .WithLocation(0)
            .WithArguments("Worker");

        await Verify.VerifyWithEfCoreAsync(source, expected);
    }

    // ===========================================================
    // Negative Cases (5+)
    // ===========================================================

    [Fact]
    public async Task BackgroundService_WithScopeFactory_NoReport()
    {
        var source = Preamble + @"
class OrderProcessor : BackgroundService
{
    private readonly AppDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    public OrderProcessor(AppDbContext db, IServiceScopeFactory sf) { _db = db; _scopeFactory = sf; }

    protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken ct)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task BackgroundService_NoTenantEntities_NoReport()
    {
        var source = @"
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.Hosting
{
    public abstract class BackgroundService
    {
        protected abstract System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken stoppingToken);
    }
}

public class AuditLog
{
    public int Id { get; set; }
    public string Message { get; set; }
}

public class LogDbContext : DbContext
{
    public DbSet<AuditLog> Logs { get; set; }
}

class Worker : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly LogDbContext _db;
    public Worker(LogDbContext db) { _db = db; }

    protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken ct)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task RegularService_WithDbContext_NoReport()
    {
        var source = Preamble + @"
class OrderService
{
    private readonly AppDbContext _db;
    public OrderService(AppDbContext db) { _db = db; }

    void DoWork() { }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }

    [Fact]
    public async Task BackgroundService_CrossTenant_NoReport()
    {
        var source = Preamble + @"
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
class CrossTenantAttribute : System.Attribute
{
    public CrossTenantAttribute(string reason = """") { }
}

[{|#0:CrossTenant(""batch job"")|}]
class OrderProcessor : BackgroundService
{
    private readonly AppDbContext _db;
    public OrderProcessor(AppDbContext db) { _db = db; }

    protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken ct)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
";
        // TL008 is suppressed by [CrossTenant], but TL902 fires for class-level usage
        var expectedTl902 = Verify.Diagnostic(Descriptors.TL902)
            .WithLocation(0)
            .WithArguments("OrderProcessor");

        await Verify.VerifyWithEfCoreAsync(source, expectedTl902);
    }

    [Fact]
    public async Task BackgroundService_WithServiceProvider_NoReport()
    {
        var source = Preamble + @"
class OrderProcessor : BackgroundService
{
    private readonly AppDbContext _db;
    private readonly System.IServiceProvider _sp;
    public OrderProcessor(AppDbContext db, System.IServiceProvider sp) { _db = db; _sp = sp; }

    protected override System.Threading.Tasks.Task ExecuteAsync(System.Threading.CancellationToken ct)
    {
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
";
        await Verify.VerifyWithEfCoreAsync(source);
    }
}
