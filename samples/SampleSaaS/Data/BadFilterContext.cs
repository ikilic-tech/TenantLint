namespace SampleSaaS.Data;

using Microsoft.EntityFrameworkCore;
using SampleSaaS.Models;

/// <summary>
/// Demonstrates TL012: HasQueryFilter referencing a value not rooted in the DbContext instance.
/// EF Core caches the model — the first value is baked in and reused for every tenant.
/// </summary>
public class BadFilterContext : DbContext
{
    public static string CurrentTenantId = "t1"; // TL007: deliberate — static tenant state

    public DbSet<Order> Orders { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite("Data Source=bad.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TL012: deliberate — static field value is baked in by EF Core's model cache
        modelBuilder.Entity<Order>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
    }
}
