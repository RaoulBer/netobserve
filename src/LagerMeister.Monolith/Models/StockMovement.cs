namespace LagerMeister.Monolith.Models;

// Movement history for an item. Each movement happened at a Location.
// GET /api/items/{id} loads each movement's Location in a separate query — the
// intentional N+1 that makes the item-detail trace interesting.
public class StockMovement
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public int LocationId { get; set; }
    public int Delta { get; set; }
    public DateTime OccurredAt { get; set; }
}
