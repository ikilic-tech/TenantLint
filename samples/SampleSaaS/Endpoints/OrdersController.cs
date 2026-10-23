namespace SampleSaaS.Endpoints;

using Microsoft.AspNetCore.Mvc;
using SampleSaaS.Data;
using SampleSaaS.Models;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _db;

    public OrdersController(AppDbContext db) => _db = db;

    // TL004: deliberate — tenant identity received from client header input
    [HttpGet]
    public IActionResult GetOrders([FromHeader] string tenantId)
    {
        return Ok(_db.Orders.Where(o => o.TenantId == tenantId).ToList());
    }
}
