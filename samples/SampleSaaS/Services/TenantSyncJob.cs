namespace SampleSaaS.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using SampleSaaS.Data;

// TL008: deliberate — background service uses tenant-scoped DbContext without creating a scope
public class TenantSyncJob : BackgroundService
{
    private readonly AppDbContext _db;

    public TenantSyncJob(AppDbContext db) => _db = db;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var orders = await _db.Orders.ToListAsync(stoppingToken);
    }
}
