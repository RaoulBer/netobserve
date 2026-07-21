namespace LagerMeister.Inventory.Domain;

// Aggregate root. Encapsulates its movement collection (no public mutation) and its
// invariants (non-negative stock). Movements are always exposed newest-first.
public sealed class InventoryItem
{
    public const int LowStockThreshold = 10;

    private readonly List<StockMovement> _movements;

    public int Id { get; }
    public Sku Sku { get; }
    public string Name { get; }
    public int StockOnHand { get; }
    public Money UnitPrice { get; }

    public IReadOnlyList<StockMovement> Movements => _movements;

    public InventoryItem(int id, Sku sku, string name, int stockOnHand, Money unitPrice,
        IEnumerable<StockMovement> movements)
    {
        if (stockOnHand < 0)
            throw new ArgumentException("Stock on hand cannot be negative.", nameof(stockOnHand));

        Id = id;
        Sku = sku ?? throw new ArgumentNullException(nameof(sku));
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Name required.", nameof(name)) : name;
        StockOnHand = stockOnHand;
        UnitPrice = unitPrice ?? throw new ArgumentNullException(nameof(unitPrice));
        _movements = (movements ?? Enumerable.Empty<StockMovement>())
            .OrderByDescending(m => m.OccurredAt)
            .ToList();
    }

    public bool IsLowStock() => StockOnHand <= LowStockThreshold;

    // Total units received (positive movements) across the known history.
    public int RecentInboundUnits() => _movements.Where(m => m.Delta > 0).Sum(m => m.Delta);
}
