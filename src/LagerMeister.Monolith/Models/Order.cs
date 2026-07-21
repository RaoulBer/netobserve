namespace LagerMeister.Monolith.Models;

public class Order
{
    public int Id { get; set; }
    public string CustomerRef { get; set; } = "";
    public string Status { get; set; } = "PENDING";
    public DateTime CreatedAt { get; set; }

    public List<OrderLine> Lines { get; set; } = new();
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
}
