namespace SampleSaaS.Models;

public class Invoice
{
    public int Id { get; set; }
    public string TenantId { get; set; } = "";
    public int OrderId { get; set; }
    public decimal Total { get; set; }
}
