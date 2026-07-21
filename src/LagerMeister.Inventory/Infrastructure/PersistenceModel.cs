using System.ComponentModel.DataAnnotations.Schema;

namespace LagerMeister.Inventory.Infrastructure;

// Persistence records — plain EF types mapped to the monolith's existing tables.
// Kept SEPARATE from the domain aggregate (the repository maps records -> domain),
// so EF concerns never leak into the domain model. This is the anti-corruption
// boundary: the new service reads legacy tables through its own clean model.

[Table("Items")]
public class ItemRecord
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int QuantityOnHand { get; set; }
    public decimal UnitPrice { get; set; }
}

[Table("StockMovements")]
public class StockMovementRecord
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int LocationId { get; set; }
    public int Delta { get; set; }
    public DateTime OccurredAt { get; set; }
}

[Table("Locations")]
public class LocationRecord
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
}
