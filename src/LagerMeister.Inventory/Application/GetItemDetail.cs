using LagerMeister.Inventory.Domain;
using LagerMeister.Inventory.Observability;

namespace LagerMeister.Inventory.Application;

public record MovementDto(int Delta, DateTime OccurredAt, string Location);

public record ItemDetailDto(
    int Id,
    string Sku,
    string Name,
    int QuantityOnHand,
    decimal UnitPrice,
    bool LowStock,
    IReadOnlyList<MovementDto> Movements);

// Application service (query handler). Wraps the lookup in a manual span and maps the
// domain aggregate to the public DTO — the DTO is deliberately shaped like the
// monolith's response so the facade swap is transparent to the caller.
public class GetItemDetail
{
    private readonly IInventoryRepository _repository;
    private readonly ILogger<GetItemDetail> _log;

    public GetItemDetail(IInventoryRepository repository, ILogger<GetItemDetail> log)
    {
        _repository = repository;
        _log = log;
    }

    public async Task<ItemDetailDto?> HandleAsync(int id, CancellationToken ct = default)
    {
        using var activity = Diagnostics.ActivitySource.StartActivity("inventory.lookup");
        activity?.SetTag(AppAttributes.ItemId, id);

        var item = await _repository.FindAsync(id, ct);
        if (item is null)
        {
            _log.LogWarning("Item {ItemId} not found", id);
            return null;
        }

        activity?.SetTag(AppAttributes.ItemSku, item.Sku.Value);
        activity?.SetTag(AppAttributes.ItemLowStock, item.IsLowStock());
        activity?.SetTag(AppAttributes.MovementCount, item.Movements.Count);

        _log.LogInformation("Item {ItemId} ({Sku}) served with {MovementCount} movements",
            item.Id, item.Sku.Value, item.Movements.Count);

        return new ItemDetailDto(
            item.Id,
            item.Sku.Value,
            item.Name,
            item.StockOnHand,
            item.UnitPrice.Amount,
            item.IsLowStock(),
            item.Movements.Select(m => new MovementDto(m.Delta, m.OccurredAt, m.LocationCode)).ToList());
    }
}
