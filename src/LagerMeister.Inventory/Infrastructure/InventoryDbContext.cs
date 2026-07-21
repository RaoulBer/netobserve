using Microsoft.EntityFrameworkCore;

namespace LagerMeister.Inventory.Infrastructure;

// Read-only context over the legacy tables. It does NOT own the schema (the monolith
// creates it) — no migrations, no EnsureCreated. Just reads.
public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options) { }

    public DbSet<ItemRecord> Items => Set<ItemRecord>();
    public DbSet<StockMovementRecord> StockMovements => Set<StockMovementRecord>();
    public DbSet<LocationRecord> Locations => Set<LocationRecord>();
}
