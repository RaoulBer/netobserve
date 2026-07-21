namespace LagerMeister.Inventory.Domain;

// Repository port (domain-owned interface). Infrastructure provides the EF adapter.
public interface IInventoryRepository
{
    Task<InventoryItem?> FindAsync(int id, CancellationToken ct = default);
}
