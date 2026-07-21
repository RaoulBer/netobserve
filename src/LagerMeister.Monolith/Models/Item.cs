namespace LagerMeister.Monolith.Models;

// Legacy anemic entity — public setters, EF-owned. No domain invariants enforced here.
public class Item
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int QuantityOnHand { get; set; }
    public decimal UnitPrice { get; set; }

    public List<StockMovement> Movements { get; set; } = new();
}
