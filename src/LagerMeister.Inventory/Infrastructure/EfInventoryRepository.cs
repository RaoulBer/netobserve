using LagerMeister.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace LagerMeister.Inventory.Infrastructure;

// EF adapter for the domain's IInventoryRepository.
//
// N+1 FIXED (vs. the monolith): the monolith loads movements, then issues one query
// PER movement to resolve its Location. Here the movements + their location codes are
// fetched in a SINGLE LEFT JOIN — the item-detail trace drops from 1+1+N SQL spans to
// just 2. This is the migration's visible payoff in Tempo.
public class EfInventoryRepository : IInventoryRepository
{
    private readonly InventoryDbContext _db;

    public EfInventoryRepository(InventoryDbContext db) => _db = db;

    public async Task<InventoryItem?> FindAsync(int id, CancellationToken ct = default)
    {
        var record = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (record is null)
            return null;

        var movements = await (
            from m in _db.StockMovements.AsNoTracking()
            join l in _db.Locations.AsNoTracking() on m.LocationId equals l.Id into loc
            from l in loc.DefaultIfEmpty()
            where m.ItemId == id
            orderby m.OccurredAt descending
            select new StockMovement(m.Delta, m.OccurredAt, l != null ? l.Code : "UNKNOWN"))
            .ToListAsync(ct);

        return new InventoryItem(
            record.Id,
            Sku.Create(record.Sku),
            record.Name,
            record.QuantityOnHand,
            Money.Euro(record.UnitPrice),
            movements);
    }
}
