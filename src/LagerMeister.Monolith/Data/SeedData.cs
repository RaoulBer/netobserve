using LagerMeister.Monolith.Models;
using Microsoft.EntityFrameworkCore;

namespace LagerMeister.Monolith.Data;

// Deterministic seed so the demo endpoints always return the same, trace-worthy data.
// Uses EnsureCreated (no migrations) — fine for a synthetic, disposable database.
public static class SeedData
{
    public static async Task InitializeAsync(WarehouseDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        if (await db.Items.AnyAsync())
            return;

        var locations = new[]
        {
            new Location { Id = 1, Code = "RECEIVING", Description = "Inbound receiving dock" },
            new Location { Id = 2, Code = "AISLE-A", Description = "High-turnover aisle A" },
            new Location { Id = 3, Code = "AISLE-B", Description = "Bulk storage aisle B" },
        };
        db.Locations.AddRange(locations);

        var items = new[]
        {
            new Item { Id = 1, Sku = "WIDGET-001", Name = "Standard Widget", QuantityOnHand = 120, UnitPrice = 4.50m },
            new Item { Id = 2, Sku = "GEAR-014",   Name = "Steel Gear 14T",  QuantityOnHand = 40,  UnitPrice = 12.75m },
            new Item { Id = 3, Sku = "BOLT-M8",    Name = "M8 Hex Bolt",     QuantityOnHand = 5000, UnitPrice = 0.08m },
            new Item { Id = 4, Sku = "CRATE-XL",   Name = "XL Shipping Crate", QuantityOnHand = 8, UnitPrice = 22.00m },
        };
        db.Items.AddRange(items);

        // A few movements per item so GET /api/items/{id} exercises the N+1.
        var movements = new List<StockMovement>();
        var baseTime = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        foreach (var item in items)
        {
            for (var n = 0; n < 3; n++)
            {
                movements.Add(new StockMovement
                {
                    ItemId = item.Id,
                    LocationId = (n % 3) + 1,
                    Delta = (n + 1) * 5,
                    OccurredAt = baseTime.AddDays(item.Id).AddHours(n)
                });
            }
        }
        db.StockMovements.AddRange(movements);

        await db.SaveChangesAsync();
    }
}
