namespace SampleSaaS.Data;

using Microsoft.EntityFrameworkCore;
using SampleSaaS.Models;

public class AppDbContext : DbContext
{
    private readonly string _tenantId;

    public AppDbContext(string tenantId)
    {
        _tenantId = tenantId;
    }

    public DbSet<Customer> Customers { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<Invoice> Invoices { get; set; } // TL005: deliberate — no query filter for tenant-scoped entity
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<Country> Countries { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite("Data Source=sample.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasQueryFilter(e => e.TenantId == _tenantId);
        modelBuilder.Entity<Order>().HasQueryFilter(e => e.TenantId == _tenantId);
        // Invoice intentionally has no filter — triggers TL005
    }
}
