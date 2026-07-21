using LagerMeister.Monolith.Models;
using Microsoft.EntityFrameworkCore;

namespace LagerMeister.Monolith.Data;

// Legacy data access: a single DbContext used directly from controllers/services.
// No repository layer, no read models, no CQRS — intentional for the "before" state.
public class WarehouseDbContext : DbContext
{
    public WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Item>().HasIndex(i => i.Sku).IsUnique();
        b.Entity<Item>().Property(i => i.UnitPrice).HasPrecision(12, 2);
    }
}
