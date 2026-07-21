using LagerMeister.Monolith.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LagerMeister.Monolith.Controllers;

[ApiController]
[Route("api/items")]
public class ItemsController : ControllerBase
{
    private readonly WarehouseDbContext _db;
    private readonly ILogger<ItemsController> _log;

    public ItemsController(WarehouseDbContext db, ILogger<ItemsController> log)
    {
        _db = db;
        _log = log;
    }

    // GET /api/items — flat inventory list (one query).
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var items = await _db.Items
            .OrderBy(i => i.Sku)
            .Select(i => new { i.Id, i.Sku, i.Name, i.QuantityOnHand, i.UnitPrice })
            .ToListAsync();
        _log.LogInformation("Listed {Count} inventory items", items.Count);
        return Ok(items);
    }

    // GET /api/items/{id} — item detail WITH an intentional N+1:
    // load the item, load its movements, then load each movement's Location in a
    // separate query. This is the "interesting" trace: 1 + 1 + N SQL spans.
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == id);
        if (item is null)
        {
            _log.LogWarning("Item {ItemId} not found", id);
            return NotFound();
        }

        var movements = await _db.StockMovements
            .Where(m => m.ItemId == id)
            .OrderByDescending(m => m.OccurredAt)
            .ToListAsync();

        var movementViews = new List<object>();
        foreach (var m in movements)
        {
            // N+1 on purpose: one round-trip per movement to resolve its Location.
            var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == m.LocationId);
            movementViews.Add(new
            {
                m.Id,
                m.Delta,
                m.OccurredAt,
                Location = location?.Code ?? "UNKNOWN"
            });
        }

        _log.LogInformation("Item {ItemId} detail served with {MovementCount} movements", id, movements.Count);
        return Ok(new
        {
            item.Id,
            item.Sku,
            item.Name,
            item.QuantityOnHand,
            item.UnitPrice,
            Movements = movementViews
        });
    }
}
