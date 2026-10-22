namespace SampleSaaS.Services;

using Microsoft.EntityFrameworkCore;
using SampleSaaS.Data;
using SampleSaaS.Models;

public class ReportService
{
    private readonly AppDbContext _db;

    public ReportService(AppDbContext db) => _db = db;

    public List<Customer> GetAllCustomers()
    {
        // TL001: deliberate — IgnoreQueryFilters() removes tenant filter
        // Code fix suggests: .IgnoreQueryFilters(["SoftDelete"]) to keep the "Tenant" filter
        return _db.Customers.IgnoreQueryFilters().ToList();
    }

    public void PurgeOldOrders()
    {
        // TL002: deliberate — raw SQL references tenant table without tenant filtering
        _db.Database.ExecuteSqlRaw("DELETE FROM Orders WHERE CreatedAt < '2020-01-01'");
    }
}
