namespace SampleSaaS.Services;

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using SampleSaaS.Data;
using SampleSaaS.Models;

/// <summary>
/// Deliberate violations for each new rule. Build this project to see the diagnostics.
/// </summary>
public class ViolationsShowcase
{
    private readonly AppDbContext _db;
    private readonly IDistributedCache _cache;

    public ViolationsShowcase(AppDbContext db, IDistributedCache cache)
    {
        _db = db;
        _cache = cache;
    }

    // TL006: deliberate — cache key does not include a tenant identifier
    public byte[]? GetCachedOrders()
    {
        var orders = _db.Orders.ToList();
        return _cache.Get("all-orders");
    }

    // TL009: deliberate — SET app.tenant_id uses session scope (not LOCAL)
    public void SetRlsTenant(string tenantId)
    {
        _db.Database.ExecuteSqlRaw("SET app.tenant_id = {0}", tenantId);
    }

    // TL013: deliberate — raw ADO.NET SQL references tenant table without filtering
    public void AdoNetPurge(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Orders WHERE CreatedAt < '2020-01-01'";
        cmd.ExecuteNonQuery();
    }

    // TL011: deliberate — AddDbContextPool with constructor-only field
    public static void RegisterPooled(IServiceCollection services)
    {
        // AppDbContext._tenantId is readonly and only set in the constructor.
        // Pooled instances reuse the first tenant's value.
        services.AddDbContextPool<AppDbContext>(o => o.UseSqlite("Data Source=pooled.db"));
    }

    // TL201: deliberate — #pragma warning disable without justification comment
    public List<Customer> SuppressedWithoutReason()
    {
#pragma warning disable TL001
        return _db.Customers.IgnoreQueryFilters().ToList();
#pragma warning restore TL001
    }

    // TL202: deliberate — [SuppressMessage] without Justification property
    [SuppressMessage("TenantIsolation", "TL001")]
    public List<Customer> SuppressedMessage()
    {
        return _db.Customers.IgnoreQueryFilters().ToList();
    }
}
