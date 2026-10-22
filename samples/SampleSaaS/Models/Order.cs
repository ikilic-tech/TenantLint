namespace SampleSaaS.Models;

public class Order
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}
