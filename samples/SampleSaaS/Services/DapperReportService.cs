namespace SampleSaaS.Services;

using System.Data;
using Dapper;
using SampleSaaS.Models;

public class DapperReportService
{
    private readonly IDbConnection _connection;

    public DapperReportService(IDbConnection connection) => _connection = connection;

    public IEnumerable<Customer> GetCustomerReport()
    {
        // TL003: deliberate — Dapper query references tenant table without tenant filtering
        return _connection.Query<Customer>("SELECT * FROM Customers");
    }
}
